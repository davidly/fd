using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

class FindDuplicates
{
    // My c: drive has   4331 duplicates taking 27 gig with UseLastWrite=true and 6075 duplicates taking 69 gig otherwise.
    // My photos folder: 4453 duplicates taking 34 gig with UseLastWrite=true and 6000 duplicates taking 41 gig otherwise.
    // The net is that using UseLastWrite=true misses many duplicates, so it sould be turned off.

    const bool UseLastWrite = false;
    static bool ConsiderFileNames = true;

    static void Usage()
    {
        Console.WriteLine( "Usage: fd [-f:FILENAME] [-m:SIZE] [-n] <path> <filespec>" );
        Console.WriteLine( "  arguments:  [-f] A specific file for which duplicates are found." );
        Console.WriteLine( "              [-m] The minimum sized file to consider." );
        Console.WriteLine( "              [-n] Ignore file Names. Find duplicates based solely on size and contents." );
        Console.WriteLine( "  examples: fd" );
        Console.WriteLine( "            fd g:\\home" );
        Console.WriteLine( "            fd -m:1000000 g:\\home" );
        Console.WriteLine( "            fd g:\\home *.cr2" );
        Console.WriteLine( "            fd -f:zauner.jpg" );
        Console.WriteLine( "            fd -f:s:\\zauner.jpg z:\\ *.jpg" );
        Console.WriteLine( "            fd -n -f:s:\\zauner.jpg z:\\ *.jpg" );
        Console.WriteLine( "  default <path> is \\ (the root of the current volume)" );
        Console.WriteLine( "  default <filespec> is *.* (all files)" );
        Console.WriteLine( "  default [-m] minimum size is 0" );
        Console.WriteLine( "  App output: + indicates a duplicate was found." );
        Console.WriteLine( "              - indicates files with the same name and size aren't duplicates." );
        Console.WriteLine( "  Notes: - Duplicates assumed if name, size, and SHA256 are indentical. Bytes aren't compared." );
        Console.WriteLine( "         - -f mode finds duplicates of one file. Otherwise, the app finds all duplicates on the disk." );
        Console.WriteLine( "         - -n works for both single-file and all-files modes" );

        Environment.Exit( 1 );
    } //Usage

    public class FileLocks
    {
        // 307 is a lot, but 64 core machines are coming and memory is cheap

        public FileLocks( int size = 307 )
        {
            objects = new object[ size ];

            for ( int i = 0; i < size; i++ )
                objects[ i ] = new object();
        } //FileLocks

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long GetHashCode( string s )
        {
            ulong h = 100003u;
    
            foreach ( char c in s )
            {
                h ^= c;
                h <<= 3;
            }
    
            return 0x7fffffffffff & ( (long) h ^ s.Length );
        } //GetHashCode

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object GetLockObject( string s, long size )
        {
            long code = 1;

            if ( ConsiderFileNames )
                code = GetHashCode( s );

            code ^= size;
            long x = code % objects.Length;

            //Console.WriteLine( "lock obj for {0} is {1}", s, x );

            return objects[ x ];
        } //GetLockObject

        private readonly object [] objects;
    } //FileLocks

    public class FileInstance
    {
        public FileInstance( string fName, DateTime fLastWrite, long fLength, string fPath )
        {
            name = fName;
            lastWrite = fLastWrite;
            length = fLength;
            paths = new List<string>();
            paths.Add( fPath );
            hash = null;
            failedHash = false;
        }

        public void AddPath( string fPath )
        {
            /*
                // validate we aren't in any reparse point infinite loops. (This would be a .net bug)

                foreach ( string path in paths )
                    if ( fPath.Equals( path ) )
                        Console.WriteLine( "\nduplicate path!!!!!    " + path );

            */

            paths.Add( fPath );
        }

        public string Name
        {
            get { return name; }
        }

        public DateTime LastWrite
        {
            get { return lastWrite; }
        }

        public long Length
        {
            get { return length; }
        }

        public List<string> Paths
        {
            get { return paths; }
        }

        public string Hash
        {
            get { return hash; }

            set { hash = value; }
        }

        public bool FailedHash
        {
            get { return failedHash; }

            set { failedHash = value; }
        }

        private string name;
        private DateTime lastWrite;
        private long length;
        private List<string> paths;
        private string hash;
        private bool failedHash;
    };

    static string ComputeHash( string fullpath )
    {
        string sHash = null;

        using ( SHA256 mySHA256 = SHA256.Create() )
        {
            try
            {
                FileStream fileStream = File.OpenRead( fullpath );
                fileStream.Position = 0;

                byte[] bytes = mySHA256.ComputeHash( fileStream );

                fileStream.Close();

                StringBuilder sBuilder = new StringBuilder();

                for ( int i = 0; i < bytes.Length; i++)
                    sBuilder.Append( bytes[ i ].ToString( "x2" ) );

                sHash = sBuilder.ToString();
            }
            catch( Exception ex )
            {
                //Console.WriteLine( "\ncan't compute hash for " + fullpath );
            }
        }

        return sHash;
    } //ComputeHash

    class FileItem
    {
        public virtual void Process( FileInfo fi ) {}
    }

    class SpecificFileItem : FileItem
    {
        string hashSpecific, fullPathSpecific;
        long lenSpecific;

        public SpecificFileItem( string hash, long len, string fullPath )
        {
            hashSpecific = hash;
            lenSpecific = len;
            fullPathSpecific = fullPath;
        }

        public override void Process( FileInfo fi )
        {
            try
            {
                if ( fi.Length != lenSpecific )
                    return;
    
                string fullPath = fi.FullName.ToLower();
    
                if ( 0 == Comparer.DefaultInvariant.Compare( fullPathSpecific, fullPath ) )
                    return;
        
                string hash = ComputeHash( fullPath );
    
                if ( 0 == Comparer.DefaultInvariant.Compare( hashSpecific, hash ) )
                    Console.WriteLine( "  matching file: {0}", fullPath );
            }
            catch ( Exception ex )
            {
                Console.WriteLine( "exception {0} processing {1} (path too long?) ", ex.ToString(), fi.FullName.ToLower() );
            }
        } //Process
    } //SpecificFileItem

    class DuplicateFileItem : FileItem
    {
        FileLocks locks;
        SortedSet<FileInstance> allFiles;
        long minSize;

        public DuplicateFileItem( SortedSet<FileInstance> all, long min )
        {
            allFiles = all;
            minSize = min;
            locks = new FileLocks();
        }

        public override void Process( FileInfo fi )
        {
            try
            {
                long len = fi.Length;

                if ( len < minSize )
                    return;

                string fullPath = fi.FullName.ToLower();
                FileInstance inst = new FileInstance( fi.Name, fi.LastWriteTimeUtc, len, fullPath );
                bool fAddInst = true;
                bool found = false;

                // Use filename and size (because files like thumbs.db are everywhere)
                // This lock just helps parallelize the work by only allowing one thread at a time to
                // work on files with the same name and size.

                object lockObj = locks.GetLockObject( fi.Name.ToLower(), len );

                lock ( lockObj )
                {
                    lock ( allFiles )
                    {
                        found = allFiles.Contains( inst );
    
                        if ( !found )
                        {
                            allFiles.Add( inst );
                            fAddInst = false;
                        }
                    }
    
                    if ( found )
                    {
                        // There is at least one match of filename, size, (and perhaps last write time), but not hash yet

                        List<FileInstance> match = new List<FileInstance>();
    
                        lock ( allFiles )
                        {
                            SortedSet<FileInstance> matches = allFiles.GetViewBetween( inst, inst );
    
                            foreach ( FileInstance f in matches )
                                match.Add( f );
                        }
        
                        foreach ( FileInstance f in match )
                        {
                            if ( f.Hash == null && !f.FailedHash )
                            {
                                f.Hash = ComputeHash( f.Paths[0] );
                                if ( f.Hash == null )
                                    f.FailedHash = true;
                            }
        
                            if ( inst.Hash == null && !inst.FailedHash )
                            {
                                inst.Hash = ComputeHash( fullPath );
                                if ( inst.Hash == null )
                                    inst.FailedHash = true;
                            }
        
                            // Either hash will be null if the app doesn't have access to the file.
                            // Assume files without a hash aren't the same.
        
                            if ( ( f.Hash != null ) &&
                                 ( inst.Hash != null ) &&
                                 ( 0 == Comparer.DefaultInvariant.Compare( f.Hash, inst.Hash ) ) )
                            {
                                Console.Write( "+" );
        
                                lock ( allFiles )
                                    f.AddPath( fullPath );
        
                                fAddInst = false;
                                break;
                            }
                            else
                            {
                                Console.Write( "-" );
                            }
                        }
                    }

                    // No exact duplicate was found, so add this file (even though it may share a name a size with a non-duplicate)
    
                    if ( fAddInst )
                        lock ( allFiles )
                            allFiles.Add( inst );
                }
            }
            catch ( Exception ex )
            {
                Console.WriteLine( "exception {0} processing {1} (path too long?) ", ex.ToString(), fi.FullName.ToLower() );
            }
        } //Process
    } //DuplicateFileItem

    static void Main( string[] args )
    {
        if ( args.Count() > 3 )
            Usage();

        if ( args.Count() >= 1 )
        {
            if ( args[ 0 ].Contains( '?' ) )
                Usage();
        }

        string SpecificFilename = null;
        string root = null;
        string extension = null;
        long minSize = 0;

        for ( int i = 0; i < args.Length; i++ )
        {
            if ( '-' == args[i][0] || '/' == args[i][0] )
            {
                string argUpper = args[i].ToUpper();
                string arg = args[i];
                char c = argUpper[1];

                if ( 'F' == c )
                {
                    SpecificFilename = arg.Substring( 3 );
                }
                else if ( 'M' == c )
                {
                    if ( arg[2] != ':' )
                        Usage();

                    minSize = Convert.ToInt64( arg.Substring( 3 ) );
                }
                else if ( 'N' == c )
                    ConsiderFileNames = false;
                else
                    Usage();
            }
            else
            {
                if ( null == root )
                    root = args[i];
                else if ( null == extension )
                    extension = args[i];
                else
                    Usage();
            }
        }

        if ( null == root )
            root = @"\";

        if ( null == extension )
            extension = @"*.*";

        root = Path.GetFullPath( root );
        DirectoryInfo diRoot = new DirectoryInfo( root );

        // Find duplicates of a specific file

        if ( null != SpecificFilename )
        {
            FileInfo fiSpecific = new FileInfo( SpecificFilename );
            long lenSpecific = fiSpecific.Length;
            string fullPathSpecific = fiSpecific.FullName.ToLower();

            Console.WriteLine( "looking for files with the same size ({0} bytes) and content as {1}", lenSpecific, fullPathSpecific );

            string hashSpecific = ComputeHash( fullPathSpecific );
            SpecificFileItem fileItem = new SpecificFileItem( hashSpecific, lenSpecific, fullPathSpecific );

            EnumerateFiles( diRoot, ConsiderFileNames ? Path.GetFileName( SpecificFilename ) : extension, fileItem );

            Environment.Exit( 1 );
        }

        // Find duplicates generally

        SortedSet<FileInstance> allFiles = new SortedSet<FileInstance>( new BySortedSet() );
        DuplicateFileItem duplicateFileItem = new DuplicateFileItem( allFiles, minSize );

        EnumerateFiles( diRoot, extension, duplicateFileItem );

        Console.WriteLine("");

        SortedSet<FileInstance> dupeFiles = new SortedSet<FileInstance>( new BySizeSortedSet() );

        foreach ( FileInstance fi in allFiles )
        {
            if ( fi.Paths.Count > 1 )
            {
                dupeFiles.Add( fi );
            }
        }

        long lDupeStorage = 0;

        foreach ( FileInstance fi in dupeFiles )
        {
            Console.WriteLine( "{0:N0} {1} {2}", fi.Length, fi.LastWrite, fi.Name );

            lDupeStorage += ( fi.Length * ( fi.Paths.Count - 1 ) );

            foreach ( string path in fi.Paths )
                Console.WriteLine( "    " + path );
        }

        Console.WriteLine( "files with duplicates: " + dupeFiles.Count );
        Console.WriteLine( "bytes wasted by duplicates: " + lDupeStorage );
        Console.WriteLine( "gigabytes wasted by duplicates: " + lDupeStorage / 1000000000 );
    } //Main

    public class BySortedSet : IComparer<FileInstance>
    {
        public int Compare( FileInstance fiA, FileInstance fiB )
        {
            if ( ConsiderFileNames )
            {
                int i = Comparer.DefaultInvariant.Compare( fiA.Name, fiB.Name );

                if ( 0 != i )
                    return i;
            }

            if ( fiA.Length != fiB.Length )
            {
                if ( fiA.Length > fiB.Length )
                    return 1;

                return -1;
            }

            if ( UseLastWrite )
            {
                int i = DateTime.Compare( fiA.LastWrite, fiB.LastWrite );

                if ( 0 != i )
                    return i;
            }

            if ( fiA.Hash != null && fiB.Hash != null )
                return Comparer.DefaultInvariant.Compare( fiA.Hash, fiB.Hash );

            // if at least one hash isn't computed yet, assume files are identical until proven otherwise

            return 0;
        }
    } //BySortedSet

    public class BySizeSortedSet : IComparer<FileInstance>
    {
        public int Compare( FileInstance fiA, FileInstance fiB )
        {
            if ( fiA.Length != fiB.Length )
            {
                if ( fiA.Length > fiB.Length )
                    return 1;

                return -1;
            }

            return 0;
        }
    } //BySizeSortedSet

    // filePattern may be the name "foo.jpg" or a pattern like "*.*"

    static void EnumerateFiles( DirectoryInfo diRoot, string filePattern, FileItem fileItem )
    {
        try
        {
            Parallel.ForEach( diRoot.EnumerateDirectories(), ( childDI ) =>
            {
                EnumerateFiles( childDI, filePattern, fileItem );
            });
        }
        catch ( Exception ex )
        {
            //Console.WriteLine( "exception {0} enumerating folders", ex.ToString() );
        }

        try
        {
            Parallel.ForEach( diRoot.EnumerateFiles( filePattern ), ( fileInfo ) =>
            {
                fileItem.Process( fileInfo );
            });
        }
        catch ( Exception ex )
        {
            //Console.WriteLine( "exception {0} getting files from dir\n", ex.ToString() );
        }
    } //EnumerateFiles

} //FindDuplicates
