// ReSharper disable InconsistentNaming

using Beutl.Media.Decoding.APNG.Chunks;

namespace Beutl.Media.Decoding.APNG;

public class APNG
{
    public APNG(string fileName)
        : this(File.ReadAllBytes(fileName))
    {
    }

    public APNG(byte[] fileBytes)
    {
        DefaultImage = new Frame();
        using MemoryStream stream = new(fileBytes);

        // check file signature.
        if (!Helper.IsBytesEqual(stream.ReadBytes(Frame.Signature.Length), Frame.Signature))
            throw new Exception("File signature incorrect.");

        // Read IHDR chunk.
        IHDRChunk = new IHDRChunk(stream);
        if (IHDRChunk.ChunkType != "IHDR")
            throw new Exception("IHDR chunk must located before any other chunks.");

        // Now let's loop in chunks
        Chunk chunk;
        Frame? frame = null;
        List<Frame> frames = [];
        var otherChunks = new List<OtherChunk>();
        bool isIDATAlreadyParsed = false;
        do
        {
            if (stream.Position == stream.Length)
                throw new Exception("IEND chunk expected.");

            chunk = new Chunk(stream);

            switch (chunk.ChunkType)
            {
                case "IHDR":
                    throw new Exception("Only single IHDR is allowed.");

                case "acTL":
                    if (IsSimplePNG)
                        throw new Exception("acTL chunk must located before any IDAT and fdAT");

                    acTLChunk = new acTLChunk(chunk);
                    break;

                case "IDAT":
                    // To be an APNG, acTL must located before any IDAT and fdAT.
                    if (acTLChunk == null)
                        IsSimplePNG = true;

                    // Only default image has IDAT.
                    DefaultImage.IHDRChunk = IHDRChunk;
                    DefaultImage.AddIDATChunk(new IDATChunk(chunk));
                    isIDATAlreadyParsed = true;
                    break;

                case "fcTL":
                    // Simple PNG should ignore this.
                    if (IsSimplePNG)
                        continue;

                    if (frame is { IDATChunks.Count: 0 })
                        throw new Exception("One frame must have only one fcTL chunk.");

                    // IDAT already parsed means this fcTL is used by FRAME IMAGE.
                    if (isIDATAlreadyParsed)
                    {
                        // register current frame object and build a new frame object
                        // for next use
                        if (frame != null)
                            frames.Add(frame);
                        frame = new Frame { IHDRChunk = IHDRChunk, fcTLChunk = new fcTLChunk(chunk) };
                    }
                    // Otherwise this fcTL is used by the DEFAULT IMAGE.
                    else
                    {
                        DefaultImage.fcTLChunk = new fcTLChunk(chunk);
                    }

                    break;
                case "fdAT":
                    // Simple PNG should ignore this.
                    if (IsSimplePNG)
                        continue;

                    // fdAT is only used by frame image.
                    if (frame == null || frame.fcTLChunk == null)
                        throw new Exception("fcTL chunk expected.");

                    frame.AddIDATChunk(new fdATChunk(chunk).ToIDATChunk());
                    break;

                case "IEND":
                    // register last frame object
                    if (frame != null)
                        frames.Add(frame);

                    if (DefaultImage.IDATChunks.Count != 0)
                        DefaultImage.IENDChunk = new IENDChunk(chunk);
                    foreach (Frame f in frames)
                    {
                        f.IENDChunk = new IENDChunk(chunk);
                    }

                    break;

                default:
                    otherChunks.Add(new OtherChunk(chunk));
                    break;
            }
        } while (chunk.ChunkType != "IEND");

        // We have one more thing to do:
        // If the default image if part of the animation,
        // we should insert it into frames list.
        if (DefaultImage.fcTLChunk != null)
        {
            frames.Insert(0, DefaultImage);
            DefaultImageIsAnimated = true;
        }

        // Now we should apply every chunk in otherChunks to every frame.
        frames.ForEach(f => otherChunks.ForEach(f.AddOtherChunk));
        Frames = frames.ToArray();
    }

    /// <summary>
    ///     Indicate whether the file is a simple PNG.
    /// </summary>
    public bool IsSimplePNG { get; }

    /// <summary>
    ///     Indicate whether the default image is part of the animation
    /// </summary>
    public bool DefaultImageIsAnimated { get; private set; }

    /// <summary>
    ///     Gets the base image.
    ///     If IsSimplePNG = True, returns the only image;
    ///     if False, returns the default image
    /// </summary>
    public Frame DefaultImage { get; }

    /// <summary>
    ///     Gets the frame array.
    ///     If IsSimplePNG = True, returns empty
    /// </summary>
    public Frame[] Frames { get; }

    /// <summary>
    ///     Gets the IHDR Chunk
    /// </summary>
    public IHDRChunk IHDRChunk { get; }

    /// <summary>
    ///     Gets the acTL Chunk
    /// </summary>
    public acTLChunk? acTLChunk { get; }
}
