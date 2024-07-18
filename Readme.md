# sc3k-indexed-file

A utility for working with the indexed database file format used by SimCity 3000.

It can be downloaded from the Releases tab: https://github.com/0xC0000054/sc3k-indexed-file/releases

## System Requirements

* Windows 10+ or Linux
* [.NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## Usage

The program can be used to perform the following actions:

1. Extracting the contents of an IXF file to a directory.
2. Listing the TGI values of each entry in an IXF file.
3. Printing the number of entries in an IXF file.

These actions can be performed on either a single IXF file or on all IXF files in a root directory and its sub-directories.

### Examples

#### Extracting IXF Entries

The extract option can use either the `--extract` long form or the `-e` short form.
When extracting entries they will be placed in a folder that has the same name as the IXF file.
 
Extracting a single IXF file into an Extracted subdirectory of the application folder: `SC3KIxf -e BUILDFAM.ixf`    
Extracting a single IXF file into a subdirectory of another folder: `SC3KIxf -e BUILDFAM.ixf <output folder>`    

Extracting multiple IXF files can be accomplished by passing a root directory to be searched instead of an individual
file path: `SC3KIxf -e <SC3K root>`

By default the program will skip extraction for any IXF file that has an existing subdirectory in the output directory,
this can be overridden using the `--overwrite-existing` or `-o` option.

#### Listing the IXF Entries

The list entries option can use either the `--list-entries` long form or the `-l` short form.
The entries will be printed to standard output.

Listing the entries in a single file: `SC3KIxf -l BUILDFAM.ixf`   
Listing the entries in multiple IXF files can be accomplished by passing a root directory to be searched instead
of an individual file path: `SC3KIxf -l <SC3K root>`  

# License

This project is licensed under the terms of the MIT License.    
See [LICENSE.txt](LICENSE.txt) for more information.

## 3rd party code

[Mono.Options](https://github.com/xamarin/XamarinComponents/tree/main/XPlat/Mono.Options) - MIT License.    
[.NET Community Toolkit](https://github.com/CommunityToolkit/dotnet) - MIT License.    

# Source Code

## Prerequisites

* Visual Studio 2022 or equivalent
* [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## Building the project

* Open the solution in the `src` folder
* Build the solution
