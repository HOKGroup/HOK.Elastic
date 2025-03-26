using System.Diagnostics;

namespace HOK.Elastic.Tests;

public class TestUtils
{
    public static void CopyFilesRecursively(string sourcePath, string targetPath)
    {
        ////Now Create all of the directories
        //foreach (
        //    string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)
        //)
        //{
        //    Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
        //}

        ////Copy all the files & Replaces any files with the same name
        //foreach (
        //    string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories)
        //)
        //{
        //    File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
        //}
        if (sourcePath.EndsWith("\\")) sourcePath = sourcePath.Substring(0, sourcePath.Length - 1);
        if (targetPath.EndsWith("\\")) targetPath = targetPath.Substring(0, targetPath.Length - 1);
        ProcessStartInfo processStartInfo = new ProcessStartInfo()
        {
            UseShellExecute = true,
            CreateNoWindow = false,

            FileName = "cmd.exe",
            Arguments = $"/k robocopy.exe \"{sourcePath}\" \"{targetPath}\" *.* /e /mir /sec /secfix /r:3 /w:3"
        };

        var p =Process.Start(processStartInfo);
        p.WaitForExit();        
    }
}
