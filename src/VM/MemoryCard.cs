namespace DreamboxVM.VM;

/// <summary>
/// Wrapper around a virtual memory card formatted for the Dreambox system
/// </summary>
public class MemoryCard : IDisposable
{
    // memory cards are 4MiB, formatted with DMBC filesystem (8192 sectors)
    private const int MEMCARD_SIZE = 4194304;
    private const int SECTOR_SIZE = 512;
    private const int MEMCARD_SECTORS = MEMCARD_SIZE / SECTOR_SIZE;

    public readonly MemCardFS fs;
    private Stream _filestream;

    public MemoryCard(string path)
    {
        if (!File.Exists(path))
        {
            // create a new file for the memory card and initialize it with the DBMC file system
            _filestream = File.Open(path, FileMode.Create, FileAccess.ReadWrite);
            fs = MemCardFS.Format(path, MEMCARD_SECTORS, _filestream);

            Console.WriteLine($"New memory card created ({path})");
        }
        else
        {
            // open memory card file
            _filestream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
            fs = new MemCardFS(path, _filestream);

            Console.WriteLine($"Existing memory card loaded ({path})");
        }
    }

    public void Dispose()
    {
        fs.Dispose();
        _filestream.Close();
        _filestream.Dispose();
    }
}