using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Fonte.Core.Models;

namespace Fonte.Core.Backup;

/// <summary>
/// Password-based encryption of backup files, streamed so photos never have to fit in memory: the key is derived
/// from the password by PBKDF2-HMAC-SHA256, then the content is cut into chunks, each sealed with AES-256-GCM.
/// </summary>
/// <remarks>
/// Layout: a header (magic, version, iterations, salt, chunk size), then the chunks, each followed by its tag. Every
/// chunk authenticates the header and its position (a counter in the nonce); the last one is flagged as such and is
/// always shorter than the others. A reordered, cut or extended file therefore fails to decrypt (the STREAM
/// construction). The salt is new for every file, so a key, and with it every nonce, is never used twice.
/// </remarks>
internal static class BackupCrypto
{
    public const int CurrentVersion = 1;

    /// <summary>magic (8) + version (1) + iterations (4) + salt (16) + chunk size (4).</summary>
    public const int HeaderSize = 33;

    public const int TagSize = 16;
    public const int NonceSize = 12;

    /// <summary>OWASP recommendation for PBKDF2-HMAC-SHA256.</summary>
    private const int Iterations = 600_000;

    /// <summary>Bounds accepted when reading, so a crafted file cannot make the app spin or allocate for minutes.</summary>
    private const int MinIterations = 10_000;
    private const int MaxIterations = 5_000_000;
    private const int MinChunkSize = 1024;
    private const int MaxChunkSize = 1024 * 1024;

    private const int DefaultChunkSize = 64 * 1024;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    private static ReadOnlySpan<byte> Magic => "FONTEBAK"u8;

    /// <summary>Writes the header and returns the stream that encrypts what is written to it.</summary>
    public static async Task<EncryptingStream> StartEncryptingAsync(
        Stream output, string password, CancellationToken cancellationToken, int chunkSize = DefaultChunkSize)
    {
        var header = new byte[HeaderSize];
        Magic.CopyTo(header);
        header[8] = CurrentVersion;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(9), Iterations);
        RandomNumberGenerator.Fill(header.AsSpan(13, SaltSize));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(29), chunkSize);

        var cipher = CreateCipher(password, header.AsSpan(13, SaltSize), Iterations);
        await output.WriteAsync(header, cancellationToken);
        return new EncryptingStream(output, cipher, header, chunkSize);
    }

    /// <summary>Reads the header and returns the stream that decrypts the rest of the file.</summary>
    /// <exception cref="FonteException">Not a Fonte backup, or one from a newer version.</exception>
    public static async Task<DecryptingStream> StartDecryptingAsync(Stream input, string password, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        var read = await input.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, cancellationToken);
        var (iterations, chunkSize) = CheckHeader(header.AsSpan(0, read));

        var cipher = CreateCipher(password, header.AsSpan(13, SaltSize), iterations);
        return new DecryptingStream(input, cipher, header, chunkSize);
    }

    /// <exception cref="FonteException">Not a Fonte backup, or one from a newer version.</exception>
    public static (int Iterations, int ChunkSize) CheckHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize || !header.StartsWith(Magic) || header[8] == 0)
            throw new FonteException(FonteError.BackupInvalidFile, "Not a Fonte backup.");
        if (header[8] > CurrentVersion)
            throw new FonteException(FonteError.BackupFromNewerVersion, $"Backup version {header[8]} is not supported.");

        var iterations = BinaryPrimitives.ReadInt32BigEndian(header[9..]);
        var chunkSize = BinaryPrimitives.ReadInt32BigEndian(header[29..]);
        if (iterations is < MinIterations or > MaxIterations || chunkSize is < MinChunkSize or > MaxChunkSize)
            throw new FonteException(FonteError.BackupInvalidFile, "Invalid encryption parameters.");
        return (iterations, chunkSize);
    }

    /// <summary>The chunk's number and whether it is the last one: never the same for two chunks of a file.</summary>
    public static void FillNonce(Span<byte> nonce, uint counter, bool isLast)
    {
        nonce.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(nonce[7..], counter);
        nonce[11] = isLast ? (byte)1 : (byte)0;
    }

    private static AesGcm CreateCipher(string password, ReadOnlySpan<byte> salt, int iterations)
    {
        // Normalise so an accented password typed on another device gives the same key.
        var passwordBytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC));
        var key = new byte[KeySize];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, key, iterations, HashAlgorithmName.SHA256);
            return new AesGcm(key, TagSize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(key);
        }
    }
}

/// <summary>Encrypts what is written, one chunk at a time. <see cref="FinishAsync"/> seals the last chunk.</summary>
internal sealed class EncryptingStream(Stream output, AesGcm cipher, byte[] header, int chunkSize) : Stream
{
    private readonly byte[] _plain = new byte[chunkSize];
    private readonly byte[] _sealed = new byte[chunkSize + BackupCrypto.TagSize];
    private int _filled;
    private uint _counter;
    private bool _finished;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => !_finished;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            buffer = buffer[Fill(buffer)..];
            if (_filled == _plain.Length)
                output.Write(Seal(isLast: false).Span);
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!buffer.IsEmpty)
        {
            buffer = buffer[Fill(buffer.Span)..];
            if (_filled == _plain.Length)
                await output.WriteAsync(Seal(isLast: false), cancellationToken);
        }
    }

    /// <summary>
    /// Seals the last chunk, shorter than the others and possibly empty. Until then the file is incomplete, and
    /// reading it fails: a backup interrupted halfway can never pass for a whole one.
    /// </summary>
    public async Task FinishAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        await output.WriteAsync(Seal(isLast: true), cancellationToken);
        await output.FlushAsync(cancellationToken);
        _finished = true;
    }

    public override void Flush()
    {
        // Chunks are written as soon as they are full; a partial one can only be written by FinishAsync.
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            cipher.Dispose();
            CryptographicOperations.ZeroMemory(_plain);
        }
        _finished = true;
        base.Dispose(disposing);
    }

    private int Fill(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        var count = Math.Min(data.Length, _plain.Length - _filled);
        data[..count].CopyTo(_plain.AsSpan(_filled));
        _filled += count;
        return count;
    }

    private ReadOnlyMemory<byte> Seal(bool isLast)
    {
        Span<byte> nonce = stackalloc byte[BackupCrypto.NonceSize];
        BackupCrypto.FillNonce(nonce, _counter, isLast);
        _counter = checked(_counter + 1);

        var length = _filled;
        cipher.Encrypt(nonce, _plain.AsSpan(0, length), _sealed.AsSpan(0, length), _sealed.AsSpan(length, BackupCrypto.TagSize), header);
        _filled = 0;
        return _sealed.AsMemory(0, length + BackupCrypto.TagSize);
    }
}

/// <summary>Decrypts a backup one chunk at a time, refusing any chunk that was changed, moved or cut off.</summary>
internal sealed class DecryptingStream(Stream input, AesGcm cipher, byte[] header, int chunkSize) : Stream
{
    private readonly byte[] _sealed = new byte[chunkSize + BackupCrypto.TagSize];
    private readonly byte[] _plain = new byte[chunkSize];
    private int _position;
    private int _length;
    private uint _counter;
    private bool _ended;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        while (_position == _length && !_ended && !buffer.IsEmpty)
            Open(input.ReadAtLeast(_sealed, _sealed.Length, throwOnEndOfStream: false));
        return Take(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_position == _length && !_ended && !buffer.IsEmpty)
            Open(await input.ReadAtLeastAsync(_sealed, _sealed.Length, throwOnEndOfStream: false, cancellationToken));
        return Take(buffer.Span);
    }

    public override void Flush()
    {
    }

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            cipher.Dispose();
            CryptographicOperations.ZeroMemory(_plain);
        }
        base.Dispose(disposing);
    }

    private int Take(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, _length - _position);
        _plain.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    /// <summary>Decrypts the chunk just read: a full one has others after it, a shorter one is the last.</summary>
    private void Open(int read)
    {
        if (read < BackupCrypto.TagSize)
            throw new FonteException(FonteError.BackupDamaged, "The backup is cut off.");

        var isLast = read < _sealed.Length;
        var length = read - BackupCrypto.TagSize;
        Span<byte> nonce = stackalloc byte[BackupCrypto.NonceSize];
        BackupCrypto.FillNonce(nonce, _counter, isLast);
        try
        {
            cipher.Decrypt(nonce, _sealed.AsSpan(0, length), _sealed.AsSpan(length, BackupCrypto.TagSize), _plain.AsSpan(0, length), header);
        }
        catch (CryptographicException)
        {
            // GCM cannot tell a wrong password from a modified first chunk; past it, the password is known to be right.
            throw _counter == 0
                ? new FonteException(FonteError.BackupWrongPassword, "The backup could not be decrypted.")
                : new FonteException(FonteError.BackupDamaged, $"Chunk {_counter} of the backup is damaged.");
        }

        _counter = checked(_counter + 1);
        _position = 0;
        _length = length;
        _ended = isLast;
    }
}
