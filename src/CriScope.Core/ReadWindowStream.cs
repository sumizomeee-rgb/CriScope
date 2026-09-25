namespace CriScope.Core;

// Freeze the readable byte extent of an append-only log without blocking capture.
internal sealed class ReadWindowStream(Stream source, long length) : Stream
{
    long remaining = length;
    readonly long extent = length;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => extent;
    public override long Position { get => extent - remaining; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (remaining == 0) return 0;
        int read = source.Read(buffer, offset, (int)Math.Min(count, remaining));
        remaining -= read;
        return read;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
}
