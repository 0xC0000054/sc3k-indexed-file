////////////////////////////////////////////////////////////////////////////
//
// This file is part of sc3k-indexed-file, a utility for working with the
// indexed database file format used by SimCity 3000.
//
// Copyright (c) 2024 Nicholas Hayes
//
// This file is licensed under terms of the MIT License.
// See LICENSE.txt for more information.
//
////////////////////////////////////////////////////////////////////////////

using Mono.Options;
using System.Collections.Frozen;
using System.IO.Enumeration;

namespace SC3KIxf
{
    internal class Program
    {
        private static readonly FrozenDictionary<uint, ResourceType> KnownResourceTypes = BuildKnownResourceTypeDictionary();

        [Flags]
        private enum IxfProcessingOptions
        {
            None = 0,
            ExtractEntries = 1 << 0,
            OverwriteExistingOutput = 1 << 1,
            ListEntries = 1 << 2,
        }

        private static FrozenDictionary<uint, ResourceType> BuildKnownResourceTypeDictionary()
        {
            return Enum.GetValues<ResourceType>().ToFrozenDictionary(k => (uint)k, e => e);
        }

        static void Main(string[] args)
        {
            IxfProcessingOptions options = IxfProcessingOptions.None;
            bool showHelp = false;

            var optionSet = new OptionSet
            {
                "Usage: SC3KIXF [OPTIONS] <input file/directory> [output directory]",
                "",
                "Options:",
                {
                    "e|extract",
                    "Write the IXF file entries to a folder in the output directory. Cannot be combined with list-entries.",
                    (string v) => { if (v != null) { options |= IxfProcessingOptions.ExtractEntries; } }
                },
                {
                    "l|list-entries",
                    "Print a list of IXF entries to standard output. Cannot be combined with extract.",
                    (string v) => { if (v != null) { options |= IxfProcessingOptions.ListEntries; } }
                },
                {
                    "o|overwrite-existing",
                    "Overwrite the IXF file entries in the output directory.",
                    (string v) => { if (v != null) { options |= IxfProcessingOptions.OverwriteExistingOutput; } }
                },
                {
                    "?|help",
                    "Show the usage information.",
                    (string v) => { showHelp = v != null; }
                }
            };

            List<string> remaining = optionSet.Parse(args);

            if (showHelp)
            {
                ShowHelp(optionSet);
                return;
            }
            else if (options.HasFlag(IxfProcessingOptions.ListEntries))
            {
                // Listing and extracting entries are mutually exclusive because they write different content
                // to standard output.
                options &= ~(IxfProcessingOptions.ExtractEntries | IxfProcessingOptions.OverwriteExistingOutput);
            }

            if (remaining.Count >= 1)
            {
                string input = remaining[0];
                string output = string.Empty;

                if (options.HasFlag(IxfProcessingOptions.ExtractEntries))
                {
                    // When extracting files to the application folder, create an Extracted subdirectory.
                    // This prevents a file name conflict if the IXF file is located in the same folder as the application.
                    output = remaining.Count == 2 ? remaining[1] : Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Extracted");
                }

                if (Directory.Exists(input))
                {
                    ScanDirerctoriesForIxfFiles(input, output, options);
                }
                else
                {
                    ProcessIxfFile(input, output, Path.GetFileName(input), options);
                }
            }
            else
            {
                ShowHelp(optionSet);
            }
        }

        private static void ProcessIxfFile(string filePath,
                                           string outputRootDirectory,
                                           string relativePath,
                                           IxfProcessingOptions options)
        {
            try
            {
                using (DBIndexedFileReader file = new(filePath))
                {
                    IReadOnlyList<IndexEntry> entries = file.Entries;

                    Console.WriteLine("{0} has {1} entries", relativePath, entries.Count);

                    if (options.HasFlag(IxfProcessingOptions.ExtractEntries))
                    {
                        string outputPath = Path.Combine(outputRootDirectory, relativePath);

                        if (options.HasFlag(IxfProcessingOptions.OverwriteExistingOutput) || !Directory.Exists(outputPath))
                        {
                            Console.WriteLine("  Writing entries to {0}", outputPath);
                            file.WriteEntriesToOutputDirectory(outputPath);
                        }
                    }
                    else if (options.HasFlag(IxfProcessingOptions.ListEntries))
                    {
                        int count = entries.Count;

                        for (int i = 0; i < count; i++)
                        {
                            IndexEntry entry = entries[i];

                            if (KnownResourceTypes.TryGetValue(entry.Type, out ResourceType resourceType))
                            {
                                Console.WriteLine("  Entry {0}: Type=0x{1:X8} ({2}) Group=0x{3:X8} Instance={4:X8}",
                                                  i + 1,
                                                  entry.Type,
                                                  resourceType,
                                                  entry.Group,
                                                  entry.Instance);
                            }
                            else
                            {
                                Console.WriteLine("  Entry {0}: Type=0x{1:X8} Group=0x{2:X8} Instance={3:X8}",
                                                  i + 1,
                                                  entry.Type,
                                                  entry.Group,
                                                  entry.Instance);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading '{relativePath}':\n {ex}");
            }
        }

        private static void ScanDirerctoriesForIxfFiles(string rootDirectory,
                                                        string outputDirectory,
                                                        IxfProcessingOptions options)
        {
            SC3KIndexedFileEnumerator enumerator = new(rootDirectory);

            while (enumerator.MoveNext())
            {
                string filePath = enumerator.Current;
                string relativePath = Path.GetRelativePath(rootDirectory, filePath);

                ProcessIxfFile(filePath, outputDirectory, relativePath, options);
            }
        }

        private static void ShowHelp(OptionSet optionSet)
        {
            Console.WriteLine("Usage: SC3KIxf [OPTIONS] <input file/directory> [output directory]");
            Console.WriteLine("[output directory] is optional and used to change the root folder for the --extract command.");
            Console.WriteLine("If [output directory] is not specified the root folder will be the folder that contains the application.");
            Console.WriteLine("Options:");
            optionSet.WriteOptionDescriptions(Console.Out);
        }

        private sealed class SC3KIndexedFileEnumerator : FileSystemEnumerator<string>
        {
            public SC3KIndexedFileEnumerator(string root) : base(root, new EnumerationOptions() { RecurseSubdirectories = true })
            {
            }

            protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
            {
                return entry.FileName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                    || entry.FileName.EndsWith(".ixf", StringComparison.OrdinalIgnoreCase)
                    || entry.FileName.EndsWith(".bld", StringComparison.OrdinalIgnoreCase)
                    || entry.FileName.EndsWith(".sc3", StringComparison.OrdinalIgnoreCase)
                    || entry.FileName.EndsWith(".st3", StringComparison.OrdinalIgnoreCase)
                    || entry.FileName.EndsWith(".sct", StringComparison.OrdinalIgnoreCase)
                    || entry.FileName.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase);
            }

            protected override string TransformEntry(ref FileSystemEntry entry)
            {
                return entry.ToFullPath();
            }
        }
    }
}
