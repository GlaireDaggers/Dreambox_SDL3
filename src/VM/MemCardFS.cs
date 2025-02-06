namespace DreamboxVM.VM;

using System.Text;

/*

// DREAMBOX MEMORY CARD LAYOUT
// sectors are 512 bytes

inode {
	u64 created;		// file creation timestamp
	u64 written;		// file write timestamp
	char filename[28];	// filename (zero terminated)
	u16 fileDataStart;	// starting sector of the file data
	u16 fileDataLen;	// file length in sectors
	u8 icon[128];		// 16x16 4bpp paletted icon
	u16 color[16];		// RGB565 icon palette
}

fs_header {
	char id[4]			// 'DBMC'
	u16 free_region		// pointer to first free region (0 if none)
	u16 inode_table		// pointer to first inode table sector
}

fs_inode_table {
	u16 next			// pointer to next inode table sector (0 if none)
	u16 inodes[255]		// array of inode numbers
}

fs_free_region {
	u16 next			// pointer to next free region
	u16 length			// length of this free region in sectors
}

*/

public class MemCardFS : IDisposable
{
    public struct FileInfo
    {
        public DateTime created;
        public DateTime modified;
        public string filename;
        public int filesize;
        public byte[] icon;
        public ushort[] iconPalette;
    }

    public class MemCardStream : Stream
    {
        public override bool CanRead => read;

        public override bool CanSeek => true;

        public override bool CanWrite => write;

        public override long Length => len * 512;

        public override long Position { get => pos; set { pos = value; } }

        private MemCardFS fs;
        private ushort begin;
        private ushort len;
        private long pos;
        private bool read;
        private bool write;

        private bool modified;

        public MemCardStream(MemCardFS fs, ushort begin, ushort len, bool read, bool write)
        {
            this.fs = fs;
            this.begin = begin;
            this.len = len;
            this.read = read;
            this.write = write;
            pos = 0;
        }

        public override void Close()
        {
            Flush();
            base.Close();
        }

        public override void Flush()
        {
            if (write && modified)
            {
                modified = false;
                fs.Flush();
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!read) throw new InvalidOperationException("Cannot read a stream without read permissions");

            ushort curSector = (ushort)(begin + (pos / 512));
            fs.SelectSector(curSector);
            fs._sectorStream.Seek(pos % 512, SeekOrigin.Begin);

            int totalRead = 0;

            while (count > 0 && curSector < (begin + len))
            {
                int bytesRemainingInSector = (int)(512 - (pos % 512));
                int readCount = count;
                if (readCount > bytesRemainingInSector)
                {
                    readCount = bytesRemainingInSector;
                }
                totalRead += fs._sectorStream.Read(buffer, offset, readCount);
                pos += totalRead;
                count -= readCount;
                offset += readCount;
                fs.SelectSector(++curSector);
            }

            return totalRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin) {
                case SeekOrigin.Begin:
                    pos = offset;
                    break;
                case SeekOrigin.Current:
                    pos += offset;
                    break;
                case SeekOrigin.End:
                    pos = (len * 512) + offset;
                    break;
            }

            if (pos > (len * 512))
            {
                throw new IOException("Cannot seek beyond end of stream", (int)DreamboxErrno.ESPIPE);
            }
            else if (pos < 0)
            {
                throw new IOException("Cannot seek before start of stream", (int)DreamboxErrno.ESPIPE);
            }

            return pos;
        }

        public override void SetLength(long value)
        {
            throw new IOException("Memory card files cannot be resized");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!write) throw new InvalidOperationException("Cannot write to a stream without write permission");
            if ((pos + count) > (len * 512)) throw new IOException("Cannot write past end of file", (int)DreamboxErrno.EFBIG);

            modified = true;

            ushort curSector = (ushort)(begin + (pos / 512));
            fs.SelectSector(curSector);
            fs._sectorStream.Seek(pos % 512, SeekOrigin.Begin);

            while (count > 0)
            {
                int bytesRemainingInSector = (int)(512 - (pos % 512));
                int writeCount = count;
                if (writeCount > bytesRemainingInSector)
                {
                    writeCount = bytesRemainingInSector;
                }
                fs._sectorWriter.Write(buffer, offset, writeCount);
                pos += writeCount;
                count -= writeCount;
                offset += writeCount;
                fs.FlushSector();
                fs.SelectSector(++curSector);
            }
        }
    }

    private struct FSHeader
    {
        public string id;
        public ushort free_region;
        public ushort inode_table;
    }

    private struct INode
    {
        public ulong created;
        public ulong written;
        public string filename;
        public ushort fileDataStart;
        public ushort fileDataLength;
        public byte[] icon;
        public ushort[] iconPalette;
    }

    private Stream _stream;
    private MemoryStream _wrapStream;
    private byte[] _sectorBuffer;
    private MemoryStream _sectorStream;
    private BinaryReader _sectorReader;
    private BinaryWriter _sectorWriter;
    private int _curSector;

    private FSHeader _header;
    private FSHeader _backup_header;

    private string _name;

    public MemCardFS(string name, Stream stream)
    {
        _name = name;

        _stream = stream;
        _wrapStream = new MemoryStream();
        _stream.CopyTo(_wrapStream);

        _sectorBuffer = new byte[512];
        _sectorStream = new MemoryStream(_sectorBuffer);

        _sectorReader = new BinaryReader(_sectorStream, Encoding.UTF8, true);
        _sectorWriter = new BinaryWriter(_sectorStream, Encoding.UTF8, true);

        SelectSector(0);

        byte[] idBytes = _sectorReader.ReadBytes(4);
        _header.id = Encoding.ASCII.GetString(idBytes);

        if (_header.id != "DBMC")
        {
            throw new IOException("Input must be formatted as DBMC filesystem");
        }

        _header.free_region = _sectorReader.ReadUInt16();
        _header.inode_table = _sectorReader.ReadUInt16();

        _backup_header = _header;

        SelectSector(0);
    }

    public static MemCardFS Format(string name, int sectors, Stream stream)
    {
        using (MemoryStream newfs = new MemoryStream(new byte[sectors * 512]))
        using (BinaryWriter newfsWriter = new BinaryWriter(newfs))
        {
            newfsWriter.Write(Encoding.ASCII.GetBytes("DBMC"));
            // first free region starts at sector 2
            newfsWriter.Write((ushort)2);
            // first inode table sector starts at sector 1
            newfsWriter.Write((ushort)1);

            // init first free region (starts at sector 2 and extends to end of memory card)
            newfs.Seek(512 * 2, SeekOrigin.Begin);
            newfsWriter.Write((ushort)0);
            newfsWriter.Write((ushort)(sectors - 2));

            stream.Write(newfs.ToArray());
            stream.Flush();
        }

        stream.Seek(0, SeekOrigin.Begin);
        return new MemCardFS(name, stream);
    }

    public static ulong DateTimeToTimestamp(DateTime dt)
    {
        return (ulong)((DateTimeOffset)dt).ToUnixTimeSeconds();
    }

    public static DateTime TimestampToDateTime(ulong timestamp)
    {
        return DateTimeOffset.FromUnixTimeSeconds((long)timestamp).LocalDateTime;
    }

    public void Dispose()
    {
        FlushSector();
        Flush();
        _wrapStream.Dispose();
        _sectorStream.Dispose();
        _sectorReader.Dispose();
        _sectorWriter.Dispose();
    }

    public bool Exists(string filename)
    {
        return GetFileData(filename, out _) != null;
    }

    public FileInfo[] GetFiles()
    {
        List<FileInfo> files = new List<FileInfo>();

        ushort inode_table_ptr = _header.inode_table;

        while (inode_table_ptr != 0)
        {
            SelectSector(inode_table_ptr);

            ushort nextTable = _sectorReader.ReadUInt16();

            ushort[] inodes = new ushort[255];
            for (int i = 0; i < inodes.Length; i++)
            {
                inodes[i] = _sectorReader.ReadUInt16();
            }

            for (int i = 0; i < inodes.Length; i++)
            {
                if (inodes[i] == 0) continue;

                // read inode data
                SelectSector(inodes[i]);
                ulong inode_created = _sectorReader.ReadUInt64();
                ulong inode_written = _sectorReader.ReadUInt64();
                string inode_filename = Encoding.UTF8.GetString(_sectorReader.ReadBytes(28)).TrimEnd('\0');
                ushort inode_filestart = _sectorReader.ReadUInt16();
                ushort inode_filelen = _sectorReader.ReadUInt16();
                byte[] inode_icon = _sectorReader.ReadBytes(128);
                ushort[] inode_iconPalette = new ushort[16];
                for (int j = 0; j < 16; j++)
                {
                    inode_iconPalette[j] = _sectorReader.ReadUInt16();
                }

                files.Add(new FileInfo
                {
                    created = TimestampToDateTime(inode_created),
                    modified = TimestampToDateTime(inode_written),
                    filename = inode_filename,
                    filesize = inode_filelen * 512,
                    icon = inode_icon,
                    iconPalette = inode_iconPalette,
                });
            }

            inode_table_ptr = nextTable;
        }

        return files.ToArray();
    }

    public FileInfo GetFileInfo(string filename)
    {
        INode? filedata = GetFileData(filename, out _);
        if (filedata == null)
        {
            throw new FileNotFoundException();
        }

        return new FileInfo()
        {
            created = TimestampToDateTime(filedata.Value.created),
            modified = TimestampToDateTime(filedata.Value.written),
            filename = filedata.Value.filename,
            filesize = filedata.Value.fileDataLength * 512,
            icon = filedata.Value.icon,
            iconPalette = filedata.Value.iconPalette,
        };
    }

    public Stream OpenRead(string filename)
    {
        INode? filedata = GetFileData(filename, out _);
        if (filedata == null)
        {
            throw new FileNotFoundException();
        }

        return new MemCardStream(this, filedata.Value.fileDataStart, filedata.Value.fileDataLength, true, false);
    }

    public Stream OpenWrite(string filename)
    {
        ulong timestamp = DateTimeToTimestamp(DateTime.Now);
        INode? filedata = GetFileData(filename, out var inodePtr);
        if (filedata == null)
        {
            throw new FileNotFoundException();
        }

        // patch inode to update write time
        SelectSector(inodePtr);
        _sectorWriter.Seek(8, SeekOrigin.Begin);
        _sectorWriter.Write((ulong)timestamp);
        FlushSector();
        Flush();

        return new MemCardStream(this, filedata.Value.fileDataStart, filedata.Value.fileDataLength, false, true);
    }

    public Stream OpenCreate(string filename, byte[] icon, ushort[] iconPalette, int length)
    {
        if (Exists(filename))
        {
            throw new IOException("File already exists", (int)DreamboxErrno.EEXIST);
        }

        ulong timestamp = DateTimeToTimestamp(DateTime.Now);
        ushort filePtr = AllocateFile(timestamp, timestamp, filename, icon, iconPalette, (ushort)(length / 512));
        if (filePtr == 0)
        {
            throw new IOException("Failed to allocate storage space for file", (int)DreamboxErrno.ENOSPC);
        }

        return new MemCardStream(this, filePtr, (ushort)(length / 512), false, true);
    }

    private void BeginChange()
    {
        _backup_header = _header;
    }

    private void EndChange()
    {
        Flush();
    }

    private void RevertChange()
    {
        _header = _backup_header;
        _wrapStream.Seek(0, SeekOrigin.Begin);
        _stream.Seek(0, SeekOrigin.Begin);
        _stream.CopyTo(_wrapStream);
        SelectSector(_curSector);
    }

    private INode? GetFileData(string name, out ushort ptr)
    {
        ushort inode_table_ptr = _header.inode_table;

        while (inode_table_ptr != 0)
        {
            SelectSector(inode_table_ptr);
            
            ushort nextTable = _sectorReader.ReadUInt16();
            ushort[] inodes = new ushort[255];

            for (int i = 0; i < inodes.Length; i++)
            {
                inodes[i] = _sectorReader.ReadUInt16();
            }

            // iterate each file and search for input filename
            for (int i = 0; i < inodes.Length; i++)
            {
                if (inodes[i] == 0) continue;

                SelectSector(inodes[i]);

                ulong inode_created = _sectorReader.ReadUInt64();
                ulong inode_written = _sectorReader.ReadUInt64();
                string inode_filename = Encoding.UTF8.GetString(_sectorReader.ReadBytes(28)).TrimEnd('\0');

                // found it
                if (inode_filename == name) {
                    ushort inode_filestart = _sectorReader.ReadUInt16();
                    ushort inode_filelen = _sectorReader.ReadUInt16();
                    byte[] inode_icon = _sectorReader.ReadBytes(128);
                    ushort[] inode_iconPalette = new ushort[16];
                    for (int j = 0; j < 16; j++) {
                        inode_iconPalette[j] = _sectorReader.ReadUInt16();
                    }

                    ptr = inodes[i];
                    return new INode()
                    {
                        created = inode_created,
                        written = inode_written,
                        filename = inode_filename,
                        fileDataStart = inode_filestart,
                        fileDataLength = inode_filelen,
                        icon = inode_icon,
                        iconPalette = inode_iconPalette,
                    };
                }
            }

            inode_table_ptr = nextTable;
        }

        // could not find file
        ptr = 0;
        return null;
    }

    // erase a file by its inode number
    private void DeleteFile(ushort inode)
    {
        // read file start+length
        SelectSector(inode);
        _sectorStream.Seek(44, SeekOrigin.Begin);
        ushort fileStart = _sectorReader.ReadUInt16();
        ushort fileLength = _sectorReader.ReadUInt16();

        // return inode
        FreeSectorRange(inode, 1);

        // return file block
        FreeSectorRange(fileStart, fileLength);

        // erase inode number from inode list
        ushort inodeTablePtr = _header.inode_table;

        while (inodeTablePtr != 0)
        {
            SelectSector(inodeTablePtr);
            ushort nextTablePtr = _sectorReader.ReadUInt16();

            for (int i = 0; i < 255; i++)
            {
                if (_sectorReader.ReadUInt16() == inode)
                {
                    _sectorStream.Seek(2 + (i * 2), SeekOrigin.Begin);
                    _sectorWriter.Write((ushort)0);
                    FlushSector();
                    Flush();
                    return;
                }
            }

            inodeTablePtr = nextTablePtr;
        }

        // if execution reaches this point it's because the inode number was not found in the inode table
        // could indicate filesystem corruption
        Console.WriteLine("Tried to erase inode but inode number was not found in table. This could indicate a corrupt filesystem");
    }

    // attempt to allocate a new file with the given metadata and size
    private ushort AllocateFile(ulong created, ulong written, string filename, byte[] icon, ushort[] iconPalette, ushort fileLen)
    {
        BeginChange();

        ushort filePtr = AllocateSectorRange(fileLen);
        if (filePtr == 0)
        {
            RevertChange();
            return 0;
        }

        ushort inodePtr = AllocateInode(created, written, filename, filePtr, fileLen, icon, iconPalette);
        if (inodePtr == 0)
        {
            RevertChange();
            return 0;
        }

        if (!AppendInodeNumber(inodePtr))
        {
            RevertChange();
            return 0;
        }

        EndChange();
        return filePtr;
    }

    // allocate a new inode
    private ushort AllocateInode(ulong created, ulong written, string filename, ushort fileDataStart, ushort fileDataLen, byte[] icon, ushort[] iconPalette)
    {
        // filename cannot exceed 28 bytes
        byte[] filenameBytes = Encoding.UTF8.GetBytes(filename);
        if (filenameBytes.Length > 28)
        {
            throw new IOException("Filename cannot exceed 28 bytes", (int)DreamboxErrno.ENAMETOOLONG);
        }

        if (icon.Length != 128)
        {
            throw new InvalidOperationException("Icon must be 128 bytes");
        }

        if (iconPalette.Length != 16)
        {
            throw new InvalidOperationException("Palette must be 16 bytes");
        }

        ushort inodePtr = AllocateSectorRange(1);

        // allocation failed
        if (inodePtr == 0)
        {
            return 0;
        }

        SelectSector(inodePtr);
        _sectorWriter.Write(created);
        _sectorWriter.Write(written);
        _sectorWriter.Write(filenameBytes);
        for (int i = filenameBytes.Length; i < 28; i++) {
            _sectorWriter.Write((byte)0);
        }
        _sectorWriter.Write(fileDataStart);
        _sectorWriter.Write(fileDataLen);
        _sectorWriter.Write(icon);

        for (int i = 0; i < iconPalette.Length; i++)
        {
            _sectorWriter.Write(iconPalette[i]);
        }
        FlushSector();

        return inodePtr;
    }

    // append a new inode numer to the inode table
    private bool AppendInodeNumber(ushort inodeNumber)
    {
        ushort prevInodeTable = 0;
        ushort inodeTable = _header.inode_table;

        while (inodeTable != 0)
        {
            SelectSector(inodeTable);
            ushort nextInodeTable = _sectorReader.ReadUInt16();

            // look for an empty slot to insert inode number into
            for (int i = 0; i < 255; i++)
            {
                ushort num = _sectorReader.ReadUInt16();
                if (num == 0)
                {
                    _sectorWriter.Seek((i * 2) + 2, SeekOrigin.Begin);
                    _sectorWriter.Write(inodeNumber);
                    FlushSector();
                    return true;
                }
            }

            prevInodeTable = inodeTable;
            inodeTable = nextInodeTable;
        }

        // hit end of inode table and didnt find a slot to insert inode number into
        // try and allocate a new inode table sector

        ushort newInodeTable = AllocateSectorRange(1);

        // allocation failed
        if (newInodeTable == 0)
        {
            return false;
        }

        // format new inode table sector
        SelectSector(newInodeTable);
        _sectorWriter.Write((ushort)0);
        _sectorWriter.Write(inodeNumber);

        if (prevInodeTable == 0)
        {
            // patch header to point at new inode table sector
            SelectSector(0);
            _header.inode_table = newInodeTable;
            _sectorWriter.Seek(6, SeekOrigin.Begin);
            _sectorWriter.Write(newInodeTable);
        }
        else
        {
            // patch previous inode table to point at new inode table sector
            SelectSector(prevInodeTable);
            _sectorWriter.Write(newInodeTable);
        }

        return true;
    }

    // return a slab of sectors to the filesystem
    private void FreeSectorRange(ushort start, ushort len)
    {
        ushort prevFreeRegion = 0;
        ushort freeRegion = _header.free_region;

        while (freeRegion != 0)
        {
            SelectSector(freeRegion);

            ushort nextFreeRegion = _sectorReader.ReadUInt16();
            ushort freeRegionLength = _sectorReader.ReadUInt16();

            if (start < freeRegion)
            {
                // insert new free region here

                if ((start + len) == freeRegion)
                {
                    // merge regions together
                    ushort newLen = (ushort)(len + freeRegionLength);

                    SelectSector(start);
                    _sectorWriter.Write(nextFreeRegion);
                    _sectorWriter.Write(newLen);
                    FlushSector();

                    break;
                }
                else
                {
                    // create new free region
                    SelectSector(start);
                    _sectorWriter.Write(freeRegion);
                    _sectorWriter.Write(len);
                    FlushSector();

                    break;
                }
            }

            prevFreeRegion = freeRegion;
            freeRegion = nextFreeRegion;
        }

        // patch up previous free region to point to new free region
        if (prevFreeRegion == 0)
        {
            // patch header to point to new free region
            SelectSector(0);
            _sectorStream.Seek(4, SeekOrigin.Begin);
            _sectorWriter.Write(start);
            FlushSector();

            _header.free_region = start;
        }
        else
        {
            SelectSector(prevFreeRegion);
            _sectorWriter.Write(start);
            FlushSector();
        }
    }

    // allocate a slab of sectors from the filesystem (returns 0 on failure)
    private ushort AllocateSectorRange(int length)
    {
        ushort prevFreeRegion = 0;
        ushort freeRegion = _header.free_region;

        while (freeRegion != 0)
        {
            SelectSector(freeRegion);

            ushort nextFreeRegion = _sectorReader.ReadUInt16();
            ushort freeRegionLength = _sectorReader.ReadUInt16();

            // region can fit requested allocation - create new free region from unused space and patch up previous free region
            if (freeRegionLength >= length)
            {
                if (freeRegionLength > length)
                {
                    // write new free region sector
                    ushort newFreeRegion = (ushort)(freeRegion + length);
                    ushort newFreeRegionLength = (ushort)(freeRegionLength - length);
                    SelectSector(newFreeRegion);
                    _sectorWriter.Write(nextFreeRegion);
                    _sectorWriter.Write(newFreeRegionLength);
                    FlushSector();

                    nextFreeRegion = newFreeRegion;
                }

                if (prevFreeRegion == 0)
                {
                    // no prev region, patch header sector
                    SelectSector(prevFreeRegion);
                    _header.free_region = nextFreeRegion;
                    _sectorWriter.Seek(4, SeekOrigin.Begin);
                    _sectorWriter.Write(nextFreeRegion);
                    FlushSector();
                }
                else
                {
                    // patch previous region
                    SelectSector(prevFreeRegion);
                    _sectorWriter.Write(nextFreeRegion);
                    FlushSector();
                }

                // return pointer to start of allocated sectors
                return freeRegion;
            }

            prevFreeRegion = freeRegion;
            freeRegion = nextFreeRegion;
        }

        // failed to allocate sectors
        return 0;
    }

    private void SelectSector(int sector)
    {
        _curSector = sector;
        _wrapStream.Seek(sector * 512, SeekOrigin.Begin);
        _wrapStream.Read(_sectorBuffer, 0, 512);
        _sectorStream.Seek(0, SeekOrigin.Begin);
    }

    private void FlushSector()
    {
        _wrapStream.Seek(_curSector * 512, SeekOrigin.Begin);
        _wrapStream.Write(_sectorBuffer, 0, 512);
    }

    private void Flush()
    {
        Console.WriteLine($"Saving memory card {_name} to disk");
        _stream.Seek(0, SeekOrigin.Begin);
        _stream.Write(_wrapStream.ToArray());
    }
}