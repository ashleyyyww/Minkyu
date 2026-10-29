using CustomAlbums.Core;
using NVorbis;
using UnityEngine;

namespace CustomAlbums.Android;

internal static class AudioAssets
{
    // The shared generic resource hook must return a Texture2D here, not a Sprite.
    public static Texture2D SectionIcon()
    {
        using var stream = typeof(AudioAssets).Assembly.GetManifestResourceStream("Minkyu.SectionIcon.png") ?? throw new FileNotFoundException("Embedded section icon missing");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = "Minkyu.SectionIcon",
            hideFlags = HideFlags.DontUnloadUnusedAsset
        };
        if (!ImageConversion.LoadImage(texture, memory.ToArray()))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidDataException("Unity rejected section icon");
        }

        texture.name = "IconCustomAlbums";
        return texture;
    }

    public static AudioClip Load(AlbumRecord album, bool preview)
    {
        using var archive = MdmArchive.Open(album.Path);
        var entry = preview ? archive.DemoEntry ?? archive.MusicEntry : archive.MusicEntry;
        using var stream = archive.OpenAsset(entry);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        int channels, sampleRate, count;
        float[] samples;
        const int maxPcmSamples = 48_000 * 2 * 900;
        if (entry.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            using var decoder = new VorbisReader(memory, false);
            channels = decoder.Channels;
            sampleRate = decoder.SampleRate;
            var expected = checked(decoder.TotalSamples * channels);
            if (expected <= 0 || expected > maxPcmSamples)
                throw new InvalidDataException("Audio exceeds decoded sample limit");
            samples = new float[(int)expected];
            count = 0;
            while (count < samples.Length)
            {
                var read = decoder.ReadSamples(samples, count, samples.Length - count);
                if (read == 0)
                    break;
                count += read;
            }
        }
        else
        {
            using var decoder = new NLayer.MpegFile(memory);
            channels = decoder.Channels;
            sampleRate = decoder.SampleRate;
            var expected = decoder.Length / sizeof(float);
            if (expected <= 0 || expected > maxPcmSamples)
                throw new InvalidDataException("Audio exceeds decoded sample limit");
            samples = new float[(int)expected];
            count = 0;
            while (count < samples.Length)
            {
                var read = decoder.ReadSamples(samples, count, samples.Length - count);
                if (read == 0)
                    break;
                count += read;
            }
        }

        if (channels is < 1 or > 2 || sampleRate < 8000 || sampleRate > 192000 || count < channels)
            throw new InvalidDataException("Unsupported audio format");
        count -= count % channels;
        if (count != samples.Length)
            Array.Resize(ref samples, count);
        var clip = AudioClip.Create(album.AssetPrefix + (preview ? "_demo" : "_music"), count / channels, channels, sampleRate, false);
        if (!clip.SetData(samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidDataException("Unity rejected decoded PCM");
        }

        return clip;
    }

    public static Sprite Cover(AlbumRecord album)
    {
        using var archive = MdmArchive.Open(album.Path);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (archive.CoverEntry != null)
        {
            var bytes = archive.ReadAssetBytes(archive.CoverEntry, 32 * 1024 * 1024);
            if (bytes.Length < 24)
                throw new InvalidDataException("Truncated artwork");
            var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
            var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
            if (width == 0 || height == 0 || width > 4096 || height > 4096)
                throw new InvalidDataException("Artwork dimensions exceed 4096x4096");
            if (!ImageConversion.LoadImage(texture, bytes))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException("Unity rejected artwork");
            }
        }

        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        return sprite;
    }
}
