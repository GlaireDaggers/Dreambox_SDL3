namespace DreamboxVM.VM;

using DiscUtils;
using DiscUtils.Iso9660;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Concurrent;

public class DiscStream : Stream
{
    public const long SECTOR_SIZE = 2048;

    private Stream innerStream;
    private long position;

    public DiscStream(Stream innerStream)
    {
        this.innerStream = innerStream;
        this.position = 0;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => innerStream.Length;

    public override long Position { get => position; set => position = value; }

    private Dictionary<long, byte[]> _sectorCache = new Dictionary<long, byte[]>();

    public override void Flush()
    {
        _sectorCache.Clear();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRemaining = count;
        int totalReadCnt = 0;
        int totalSectorsRead = 0;

        while (bytesRemaining > 0)
        {
            // if sector containing cursor is already cached in memory, just read from that
            // otherwise, read new sector into cache
            long sector = position / SECTOR_SIZE;
            long sectorPos = sector * SECTOR_SIZE;

            if (!_sectorCache.TryGetValue(sector, out var sectorBytes))
            {
                sectorBytes = new byte[SECTOR_SIZE];

                // read sector into memory
                innerStream.Seek(sector * SECTOR_SIZE, SeekOrigin.Begin);
                innerStream.ReadExactly(sectorBytes, 0, (int)SECTOR_SIZE);

                totalSectorsRead++;
                _sectorCache.Add(sector, sectorBytes);
            }

            // copy sector data into dest array
            long offsetInSector = position - (sector * SECTOR_SIZE);
            long bytesRemainingInSector = SECTOR_SIZE - offsetInSector;
            long readLen = bytesRemaining;
            if (readLen > bytesRemainingInSector) readLen = bytesRemainingInSector;
            

            Buffer.BlockCopy(sectorBytes, (int)offsetInSector, buffer, offset, (int)readLen);

            bytesRemaining -= (int)readLen;
            position += readLen;
            totalReadCnt += (int)readLen;
            offset += (int)readLen;
        }

        return totalReadCnt;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        switch (origin)
        {
            case SeekOrigin.Begin:
                position = offset;
                break;
            case SeekOrigin.Current:
                position += offset;
                break;
            case SeekOrigin.End:
                position = innerStream.Length - offset;
                break;
        }

        return position;
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }
}

public interface IDiskDriver : IDisposable
{
    void Update(float dt);
    string? GetLabel();
    void Insert(Stream fs);
    void Eject();
    bool Inserted();
    DirectoryReader? OpenDirectory(string path);
    Stream OpenRead(string path);
    bool FileExists(string path);
}

public class DiskDriverWrapper : IDiskDriver
{
    public IDiskDriver? InternalDriver => _internalDriver;

    private IDiskDriver? _internalDriver;

    public void SetDriver(IDiskDriver newDriver)
    {
        _internalDriver?.Dispose();
        _internalDriver = newDriver;
    }

    public void Dispose()
    {
        _internalDriver?.Dispose();
    }

    public void Update(float dt) {
        _internalDriver?.Update(dt);
    }

    public string? GetLabel() {
        return _internalDriver?.GetLabel();
    }
    
    public void Insert(Stream fs) {
        _internalDriver?.Insert(fs);
    }
    
    public void Eject() {
        _internalDriver?.Eject();
    }
    
    public bool Inserted() {
        return _internalDriver?.Inserted() ?? false;
    }
    
    public DirectoryReader? OpenDirectory(string path) {
        return _internalDriver?.OpenDirectory(path);
    }
    
    public Stream OpenRead(string path) {
        return _internalDriver?.OpenRead(path) ?? throw new Exception("No disk mounted");
    }
    
    public bool FileExists(string path) {
        return _internalDriver?.FileExists(path) ?? false;
    }
}

public class ISODiskDriver : IDiskDriver
{
    private CDReader? _reader;
    private Stream? _cdFile;
    private DiscStream? _cdFileWrapper;

    public void Dispose()
    {
        _reader?.Dispose();
        _cdFile?.Dispose();
    }

    public void Update(float dt)
    {
    }

    public string? GetLabel()
    {
        return _reader?.VolumeLabel;
    }

    public void Insert(Stream fs)
    {
        fs.Flush();
        _cdFileWrapper = new DiscStream(fs);
        _reader = new CDReader(_cdFileWrapper, false);
        Console.WriteLine($"Valid ISO-9660 cd image detected ({_reader.VolumeLabel}), reading disc");
        _cdFile = fs;
    }

    public bool Inserted()
    {
        return _reader != null;
    }

    public void Eject()
    {
        _reader?.Dispose();
        _cdFileWrapper?.Dispose();
        _cdFile?.Dispose();

        _reader = null;
        _cdFileWrapper = null;
        _cdFile = null;
    }

    public DirectoryReader? OpenDirectory(string path)
    {
        DiscDirectoryInfo info = _reader?.GetDirectoryInfo(path.Replace('/', '\\')) ?? throw new DirectoryNotFoundException("No valid CD inserted");
        if (!info.Exists) return null;

        return new DirectoryReader(info);
    }

    public Stream OpenRead(string path)
    {
        return _reader?.OpenFile(path.Replace('/', '\\'), FileMode.Open) ?? throw new FileNotFoundException("No valid CD inserted");
    }

    public bool FileExists(string path)
    {
        return _reader?.FileExists(path.Replace('/', '\\')) ?? throw new FileNotFoundException("No valid CD inserted");
    }
}

public class WindowsCDDiskDriver : IDiskDriver
{
    const uint GENERIC_READ = 0x80000000;
    const int FILE_SHARE_READ = 0x1;
    const uint IOCTL_CDROM_SET_SPEED = 0x00024060;

    private struct DriveStatus
    {
        public bool isReady;
        public string? driveLabel;
    }

    [DllImport("winmm.dll", EntryPoint = "mciSendStringA")]
    public static extern void mciSendStringA(string lpstrCommand, StringBuilder lpstrReturnString, UInt32 uReturnLength, IntPtr hwndCallback);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr SecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile
    );

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CloseFile(IntPtr handle);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped
    );

    private struct _CDROM_SET_SPEED
    {
        public uint RequestType;
        public ushort ReadSpeed;
        public ushort WriteSpeed;
        public uint RotationControl;
    }

    private DriveInfo _driveInfo;
    private bool _cachedReady = false;
    private string? _cachedLabel = null;

    private Task _drivePollTask;
    private bool _drivePollRunning = true;

    private ConcurrentQueue<DriveStatus> _statusQueue = new ConcurrentQueue<DriveStatus>();
    private Stream? _activeDriveStream;
    private DiscStream? _activeDriveStreamWrapper;
    private CDReader? _activeReader;

    private IntPtr _driveHandle;

    public WindowsCDDiskDriver()
    {
        // try and find an attached CD-ROM drive
        foreach (var driveInfo in DriveInfo.GetDrives())
        {
            if (driveInfo.DriveType == DriveType.CDRom)
            {
                _driveInfo = driveInfo;
                Console.WriteLine("Using disk drive: " + _driveInfo.Name);
                break;
            }
        }

        if (_driveInfo == null)
        {
            throw new DriveNotFoundException();
        }

        // start background polling task
        _drivePollTask = Task.Run(PollDrive);
    }

    public void Dispose()
    {
        _drivePollRunning = false;
        _drivePollTask.Wait();

        _activeDriveStreamWrapper?.Dispose();
        _activeDriveStream?.Dispose();
        _activeReader?.Dispose();

        if (_driveHandle != IntPtr.Zero)
        {
            CloseFile(_driveHandle);
        }
    }

    public void Update(float dt)
    {
        while (_statusQueue.TryDequeue(out var result))
        {
            _cachedLabel = result.driveLabel;
            _cachedReady = result.isReady;
        }

        if (_cachedReady && _activeReader == null)
        {
            OpenDriveReader();
        }
        else if (!_cachedReady)
        {
            _activeReader?.Dispose();
            _activeReader = null;
        }
    }

    public void Eject()
    {
        _activeReader?.Dispose();
        _activeReader = null;

        StringBuilder rt = new StringBuilder();
        mciSendStringA("set CDAudio door open", rt, 127, IntPtr.Zero);
    }

    public string? GetLabel()
    {
        return _cachedLabel;
    }

    public void Insert(Stream fs)
    {
    }

    public bool Inserted()
    {
        return _cachedReady;
    }

    public DirectoryReader? OpenDirectory(string path)
    {
        DiscDirectoryInfo info = _activeReader?.GetDirectoryInfo(path.Replace('/', '\\')) ?? throw new DirectoryNotFoundException("No valid CD inserted");
        if (!info.Exists) return null;

        return new DirectoryReader(info);
    }

    public Stream OpenRead(string path)
    {
        return _activeReader?.OpenFile(path.Replace('/', '\\'), FileMode.Open) ?? throw new FileNotFoundException("No valid CD inserted");
    }

    public bool FileExists(string path)
    {
        return _activeReader?.FileExists(path.Replace('/', '\\')) ?? throw new FileNotFoundException("No valid CD inserted");
    }

    private void PollDrive()
    {
        while (_drivePollRunning)
        {
            string? driveLabel;

            try
            {
                driveLabel = _driveInfo.IsReady ? _driveInfo.VolumeLabel : null;
            }
            catch
            {
                driveLabel = null;
            }

            _statusQueue.Enqueue(new DriveStatus
            {
                driveLabel = driveLabel,
                isReady = _driveInfo.IsReady,
            });

            Thread.Sleep(1000);
        }
    }

    private void OpenDriveReader()
    {
        try
        {
            string drivePath = @"\\.\" + _driveInfo.Name.TrimEnd('\\');

            // set the drive speed to 24X
            _driveHandle = CreateFile(drivePath, GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero,
                    0x3, 0x80, IntPtr.Zero);

            if (_driveHandle != IntPtr.Zero)
            {
                // speed is given in KB/s - PCSX2 passes "3600" for CDs (equivalent to 24X reading speed)
                unsafe
                {
                    _CDROM_SET_SPEED setSpeed = new _CDROM_SET_SPEED
                    {
                        RequestType = 0x0, // CdromSetSpeed
                        ReadSpeed = 3600,
                        WriteSpeed = 3600,
                        RotationControl = 0x0, // CdromDefaultRotation
                    };
                    if (!DeviceIoControl(_driveHandle, IOCTL_CDROM_SET_SPEED, (IntPtr)(&setSpeed), (uint)sizeof(_CDROM_SET_SPEED),
                        IntPtr.Zero, 0,
                        out _, IntPtr.Zero))
                    {
                        Console.WriteLine("Failed setting CD speed");
                    }
                    else
                    {
                        Console.WriteLine("CD speed set to 24X");
                    }
                }
            }

            _activeDriveStreamWrapper?.Dispose();
            _activeDriveStream?.Dispose();
            _activeDriveStream = File.OpenRead(drivePath);
            _activeDriveStreamWrapper = new DiscStream(_activeDriveStream);
            _activeReader?.Dispose();
            _activeReader = new CDReader(_activeDriveStreamWrapper, false);
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed opening disc reader: " + e.Message);
            // failed reading disk
            _activeReader = null;
        }
    }
}

public class LinuxCDDiskDriver : IDiskDriver
{
    private struct DriveStatus
    {
        public bool isReady;
        public string? driveLabel;
    }

    private DriveInfo _driveInfo;
    private string _driveDevicePath;
    private bool _cachedReady = false;
    private string? _cachedLabel = null;

    private Task _drivePollTask;
    private bool _drivePollRunning = true;

    private ConcurrentQueue<DriveStatus> _statusQueue = new ConcurrentQueue<DriveStatus>();
    private Stream? _activeDriveStream;
    private DiscStream? _activeDriveStreamWrapper;
    private CDReader? _activeReader;

    public LinuxCDDiskDriver()
    {
        // try and find an attached CD-ROM drive
        foreach (var driveInfo in DriveInfo.GetDrives())
        {
            if (driveInfo.DriveType == DriveType.CDRom)
            {
                _driveInfo = driveInfo;
                _driveDevicePath = GetDeviceForMount(_driveInfo.RootDirectory.FullName);
                Console.WriteLine("Using disk drive: " + _driveDevicePath);
                break;
            }
        }

        if (_driveInfo == null || _driveDevicePath == null)
        {
            throw new DriveNotFoundException();
        }

        // start background polling task
        _drivePollTask = Task.Run(PollDrive);
    }

    public void Dispose()
    {
        _drivePollRunning = false;
        _drivePollTask.Wait();

        _activeDriveStreamWrapper?.Dispose();
        _activeDriveStream?.Dispose();
        _activeReader?.Dispose();
    }

    public void Update(float dt)
    {
        while (_statusQueue.TryDequeue(out var result))
        {
            _cachedLabel = result.driveLabel;
            _cachedReady = result.isReady;
        }

        if (_cachedReady && _activeReader == null)
        {
            OpenDriveReader();
        }
        else if (!_cachedReady)
        {
            _activeReader?.Dispose();
            _activeReader = null;
        }
    }

    public void Eject()
    {
        _activeReader?.Dispose();
        _activeReader = null;

        // use shell command to eject drive
        Process p = new Process();
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.FileName = "eject";
        p.StartInfo.Arguments = $"-r {_driveDevicePath}";
        p.Start();
    }

    public string? GetLabel()
    {
        return _cachedLabel;
    }

    public void Insert(Stream fs)
    {
    }

    public bool Inserted()
    {
        return _cachedReady;
    }

    public DirectoryReader? OpenDirectory(string path)
    {
        DiscDirectoryInfo info = _activeReader?.GetDirectoryInfo(path.Replace('/', '\\')) ?? throw new DirectoryNotFoundException("No valid CD inserted");
        if (!info.Exists) return null;

        return new DirectoryReader(info);
    }

    public Stream OpenRead(string path)
    {
        return _activeReader?.OpenFile(path.Replace('/', '\\'), FileMode.Open) ?? throw new FileNotFoundException("No valid CD inserted");
    }

    public bool FileExists(string path)
    {
        return _activeReader?.FileExists(path.Replace('/', '\\')) ?? throw new FileNotFoundException("No valid CD inserted");
    }

    private void PollDrive()
    {
        while (_drivePollRunning)
        {
            string? driveLabel;
            try
            {
                driveLabel = _driveInfo.IsReady ? _driveInfo.VolumeLabel : null;
            }
            catch
            {
                driveLabel = null;
            }

            _statusQueue.Enqueue(new DriveStatus
            {
                driveLabel = driveLabel,
                isReady = _driveInfo.IsReady,
            });

            Thread.Sleep(1000);
        }
    }

    private void OpenDriveReader()
    {
        try
        {
            // use shell command to set drive speed to 24X
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = "eject";
            p.StartInfo.Arguments = $"-x 24 {_driveDevicePath}";
            p.Start();

            Console.WriteLine("CD speed set to 24X");

            _activeDriveStreamWrapper?.Dispose();
            _activeDriveStream?.Dispose();
            _activeDriveStream = File.OpenRead(_driveDevicePath);
            _activeDriveStreamWrapper = new DiscStream(_activeDriveStream);
            _activeReader?.Dispose();
            _activeReader = new CDReader(_activeDriveStreamWrapper, false);
        }
        catch(Exception e)
        {
            Console.WriteLine("Failed opening disc reader: " + e.Message);
            // failed reading disk
            _activeReader = null;
        }
    }

    private string GetDeviceForMount(string mountpoint)
    {
        string[] mounts = File.ReadAllLines("/proc/mounts");

        foreach (var line in mounts)
        {
            string[] columns = line.Split(' ', '\t', '\n', '\\');
            string device = columns[0];
            string mnt = columns[1];

            if (mnt == mountpoint)
            {
                return device;
            }
        }

        throw new KeyNotFoundException();
    }
}