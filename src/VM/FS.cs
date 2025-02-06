namespace DreamboxVM.VM;

public enum DreamboxErrno
{
    ESUCCESS = 0,

    /// <summary>
    /// Argument list too long
    /// </summary>
    E2BIG,

    /// <summary>
    /// Access denied
    /// </summary>
    EACCESS,

    /// <summary>
    /// Address already in use
    /// </summary>
    EADDRINUSE,

    /// <summary>
    /// Address not available
    /// </summary>
    EADDRNOTAVAIL,

    /// <summary>
    /// Address family not supported
    /// </summary>
    EAFNOSUPPORT,

    /// <summary>
    /// Resource unavailable, or operation would block
    /// </summary>
    EAGAIN,

    /// <summary>
    /// Connection already in progress
    /// </summary>
    EALREADY,

    /// <summary>
    /// Bad file descriptor
    /// </summary>
    EBADF,

    /// <summary>
    /// Bad message
    /// </summary>
    EBADMSG,

    /// <summary>
    /// Resource or device busy
    /// </summary>
    EBUSY,

    /// <summary>
    /// Operation canceled
    /// </summary>
    ECANCELED,

    /// <summary>
    /// No child process
    /// </summary>
    ECHILD,

    /// <summary>
    /// Connection aborted
    /// </summary>
    ECONNABORTED,

    /// <summary>
    /// Connection refused
    /// </summary>
    ECONNREFUSED,

    /// <summary>
    /// Connection reset
    /// </summary>
    ECONNRESET,

    /// <summary>
    /// Resource deadlock would occur
    /// </summary>
    EDEADLK,

    /// <summary>
    /// Destination address required
    /// </summary>
    EDESTADDRREQ,

    /// <summary>
    /// Math argument out of domain of function
    /// </summary>
    EDOM,

    /// <summary>
    /// Reserved
    /// </summary>
    EDQUOT,

    /// <summary>
    /// File exists
    /// </summary>
    EEXIST,

    /// <summary>
    /// Bad address
    /// </summary>
    EFAULT,

    /// <summary>
    /// File too big
    /// </summary>
    EFBIG,

    /// <summary>
    /// Host is unreachable
    /// </summary>
    EHOSTUNREACH,

    /// <summary>
    /// Identifier removed
    /// </summary>
    EIDRM,

    /// <summary>
    /// Illegal byte sequence
    /// </summary>
    EILSEQ,

    /// <summary>
    /// Operation in progress
    /// </summary>
    EINPROGRESS,

    /// <summary>
    /// Interrupted function
    /// </summary>
    EINTR,

    /// <summary>
    /// Invalid argument
    /// </summary>
    EINVAL,

    /// <summary>
    /// IO error
    /// </summary>
    EIO,

    /// <summary>
    /// Socket is connected
    /// </summary>
    EISCONN,

    /// <summary>
    /// Is a directory
    /// </summary>
    EISDIR,

    /// <summary>
    /// Too many symbolic references
    /// </summary>
    ELOOP,

    /// <summary>
    /// File descriptor too large
    /// </summary>
    EMFILE,

    /// <summary>
    /// Too many links
    /// </summary>
    EMLINK,

    /// <summary>
    /// Message size too large
    /// </summary>
    EMSGSIZE,

    /// <summary>
    /// Reserved
    /// </summary>
    EMULTIHOP,

    /// <summary>
    /// Filename too long
    /// </summary>
    ENAMETOOLONG,

    /// <summary>
    /// Network is down
    /// </summary>
    ENETDOWN,

    /// <summary>
    /// Connection was reset by network
    /// </summary>
    ENETRESET,

    /// <summary>
    /// Network is unreachable
    /// </summary>
    ENETUNREACH,

    /// <summary>
    /// Too many files open
    /// </summary>
    ENFILE,

    /// <summary>
    /// No buffer space available
    /// </summary>
    ENOBUFS,

    /// <summary>
    /// No such device
    /// </summary>
    ENODEV,

    /// <summary>
    /// No such file or directory
    /// </summary>
    ENOENT,

    /// <summary>
    /// Executable file format error
    /// </summary>
    ENOEXEC,

    /// <summary>
    /// No locks available
    /// </summary>
    ENOLCK,

    /// <summary>
    /// Reserved,
    /// </summary>
    ENOLINK,

    /// <summary>
    /// Not enough space
    /// </summary>
    ENOMEM,

    /// <summary>
    /// No message of the desired type
    /// </summary>
    ENOMSG,

    /// <summary>
    /// Protocol not available
    /// </summary>
    ENOPROTOOPT,

    /// <summary>
    /// No space left on device
    /// </summary>
    ENOSPC,

    /// <summary>
    /// Function not supported
    /// </summary>
    ENOSYS,

    /// <summary>
    /// Socket is not connected
    /// </summary>
    ENOTCONN,

    /// <summary>
    /// Not a directory
    /// </summary>
    ENOTDIR,

    /// <summary>
    /// Directory not empty
    /// </summary>
    ENOTEMPTY,

    /// <summary>
    /// State not recoverable
    /// </summary>
    ENOTRECOVERABLE,

    /// <summary>
    /// Not a socket
    /// </summary>
    ENOTSOCK,

    /// <summary>
    /// Not supported, or operation not supported on socket
    /// </summary>
    ENOTSUP,

    /// <summary>
    /// Inappropriate I/O control operation
    /// </summary>
    ENOTTTY,

    /// <summary>
    /// No such device or address
    /// </summary>
    ENXIO,

    /// <summary>
    /// Value too large to be stored in data type
    /// </summary>
    EOVERFLOW,

    /// <summary>
    /// Previous owner died
    /// </summary>
    EOWNERDEAD,

    /// <summary>
    /// Operation not permitted
    /// </summary>
    EPERM,

    /// <summary>
    /// Broken pipe
    /// </summary>
    EPIPE,

    /// <summary>
    /// Protocol error
    /// </summary>
    EPROTO,

    /// <summary>
    /// Protocol not supported
    /// </summary>
    EPROTONOSUPPORT,

    /// <summary>
    /// Protocol wrong type for socket
    /// </summary>
    EPROTOTYPE,

    /// <summary>
    /// Result too large
    /// </summary>
    ERANGE,

    /// <summary>
    /// Read only file system
    /// </summary>
    EROFS,

    /// <summary>
    /// Invalid seek
    /// </summary>
    ESPIPE,

    /// <summary>
    /// No such process
    /// </summary>
    ESRCH,

    /// <summary>
    /// Reserved
    /// </summary>
    ESTALE,

    /// <summary>
    /// Connection timed out
    /// </summary>
    ETIMEDOUT,

    /// <summary>
    /// Text file busy
    /// </summary>
    ETXTBSY,

    /// <summary>
    /// Cross device link
    /// </summary>
    EXDEV,

    /// <summary>
    /// Capabilities insufficient
    /// </summary>
    ENOTCAPABLE,
}

public struct DirEnt
{
    public string name;
    public bool isDirectory;
    public DateTime created;
    public DateTime modified;
    public int size;
}

public class DirectoryReader
{
    private List<DirEnt> _filesAndDirs;
    private int _pos;

    public DirectoryReader(DiscUtils.DiscDirectoryInfo cdDirectory)
    {
        _filesAndDirs = new List<DirEnt>();
        foreach (var file in cdDirectory.GetFiles()) {
            _filesAndDirs.Add(new DirEnt
            {
                name = file.Name,
                isDirectory = false,
                created = file.CreationTime,
                modified = file.LastWriteTime,
                size = (int)file.Length
            });
        }
        foreach (var dir in cdDirectory.GetDirectories()) {
            _filesAndDirs.Add(new DirEnt
            {
                name = dir.Name,
                isDirectory = true,
                created = dir.CreationTime,
                modified = dir.LastWriteTime,
                size = 0
            });
        }
        _pos = 0;
    }

    public DirectoryReader(MemoryCard memoryCard)
    {
        _filesAndDirs = new List<DirEnt>();
        foreach (var file in memoryCard.fs.GetFiles()) {
            _filesAndDirs.Add(new DirEnt
            {
                name = file.filename,
                isDirectory = false,
                created = file.created,
                modified = file.modified,
                size = file.filesize,
            });
        }
    }

    public DirectoryReader(DirectoryInfo dirInfo)
    {
        _filesAndDirs = new List<DirEnt>();
        foreach (var file in dirInfo.GetFiles()) {
            _filesAndDirs.Add(new DirEnt
            {
                name = file.Name,
                isDirectory = false,
                created = file.CreationTime,
                modified = file.LastWriteTime,
                size = (int)file.Length
            });
        }
        foreach (var dir in dirInfo.GetDirectories()) {
            _filesAndDirs.Add(new DirEnt
            {
                name = dir.Name,
                isDirectory = true,
                created = dir.CreationTime,
                modified = dir.LastWriteTime,
                size = 0
            });
        }
        _pos = 0;
    }

    public DirEnt? ReadNext()
    {
        if (_pos >= _filesAndDirs.Count) return null;
        return _filesAndDirs[_pos++];
    }

    public void Rewind()
    {
        _pos = 0;
    }
}