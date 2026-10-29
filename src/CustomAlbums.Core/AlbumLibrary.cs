using System;
using System.Collections.Generic;
using System.IO;

namespace CustomAlbums.Core
{
    public sealed class AlbumLibrary
    {
        private readonly string root;
        private readonly MdmReadOptions options;
        public AlbumLibrary(string directory, MdmReadOptions? options = null)
        {
            root = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            this.options = (options ?? new MdmReadOptions()).Snapshot();
            Directory.CreateDirectory(root);
        }

        public string RootDirectory => root;

        public string Import(Stream source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            var temporary = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N") + ".pending");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    MdmArchive.CopyBounded(source, output, options.MaxArchiveBytes);
                    output.Flush(true);
                }

                string id;
                using (var archive = MdmArchive.Open(temporary, options))
                    id = archive.Id;
                var destination = Path.Combine(root, id + ".mdm");
                if (File.Exists(destination))
                {
                    using (var existing = MdmArchive.Open(destination, options))
                        if (existing.Id != id)
                            throw new InvalidDataException("Existing library entry does not match its SHA-256 filename.");
                    return destination;
                }

                try
                {
                    File.Move(temporary, destination);
                }
                catch (IOException)when (File.Exists(destination))
                {
                    using (var existing = MdmArchive.Open(destination, options))
                        if (existing.Id != id)
                            throw new InvalidDataException("Concurrent import created an invalid library entry.");
                }

                return destination;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        public IEnumerable<string> EnumeratePaths() => Directory.EnumerateFiles(root, "*.mdm", SearchOption.TopDirectoryOnly);
    }
}
