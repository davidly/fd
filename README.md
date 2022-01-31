# fd
Find Duplicates. Windows command-line app for finding duplicate files.

    Usage: fd [-f:FILENAME] [-m:SIZE] [-n] <path> <filespec>
    
      arguments:  [-f] A specific file for which duplicates are found.
                  [-m] The minimum sized file to consider.
                  [-n] Ignore file Names. Find duplicates based soley on size and contents.
      examples: fd
                fd g:\home
                fd -m:1000000 g:\home
                fd g:\home *.cr2
                fd -f:zauner.jpg
                fd -f:s:\zauner.jpg z:\ *.jpg
                fd -n -f:s:\zauner.jpg z:\ *.jpg
      default <path> is \ (the root of the current volume)
      default <filespec> is *.* (all files)
      default [-m] minimum size is 0
      App output: + indicates a duplicate was found.
                  - indicates files with the same name and size aren't duplicates.
      Notes: - Duplicates assumed if name, size, and SHA256 are indentical. Bytes aren't compared.
             - -f mode finds duplicates of one file. Otherwise, the app finds all duplicates on the disk.
             - -n works for both single-file and all-files modes

Build using your favorite version of .net. e.g.:

    c:\windows\microsoft.net\framework64\v4.0.30319\csc.exe fd.cs /nowarn:0162 /nowarn:0168 /nologo /debug:full /o+
