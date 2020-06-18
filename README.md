# TeknoParrot Web Installer

## Usage
Run TPBootstrapper.exe and download cores.

## Compiling
I recommend Visual Studio 2019, but it should work fine in any recent version of VS. It will only run on Windows, since it is written in WPF and .NET Framework rather then .NET Core or something similar.

Prerequesites are:
- Octokit.NET
- Autoupdater.NET
- Ookii.Dialogs

These could be replaced easily with custom made solutions but I decided not to reinvent the wheel.

## Todo

- Need some way to read the version information from the different DLL files rather then storing them in a data file.
- General re-organization

## Contributions
Pull Requests are welcome, as well as Issue logs, please only file them in English so we can understand them.