using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System.Text.RegularExpressions;
using System.Configuration;

namespace BeDeploy
{
    class Program
    {
        private static string _conn = ConfigurationManager.AppSettings["Conn"];
        private static string _ver = ConfigurationManager.AppSettings["Version"];
        private static string _root = ConfigurationManager.AppSettings["WebRoot"];

        static void Main(string[] args)
        {
            Console.WriteLine("starting local BE deployment...");
            
            // 1. move files from be to bedb
            ForceDeleteDirectory(_root + "\\bedb");
            var src = new DirectoryInfo(_root + "\\be");
            var tgt = new DirectoryInfo(_root + "\\bedb");
            CopyDir(src, tgt);
            Console.WriteLine("1 - done copy from /be to /bedb");

            // 2. replace web.config with DB web.config
            File.Delete(_root + "\\bedb\\Web.config");
            File.Copy(_root + "\\bedb\\setup\\SQLServer\\DbWeb.config", _root + "\\bedb\\Web.config");
            Console.WriteLine("2 - done moving db web.config");

            // 3. run DDL scripts to drop/create/populate database
            RunDDL();

            // 4. refresh /blog direcory
            ForceDeleteDirectory(_root + "\\blog");
            var src2 = new DirectoryInfo(_root + "\\_deploy\\blog");
            var tgt2 = new DirectoryInfo(_root + "\\blog");
            CopyDir(src2, tgt2);
            Console.WriteLine("4 - done refreshing /blog");

            // 5. update web.config files to use local gallery feed /v01
            var comm = "<!-- Override default application settings here -->";
            var appSet = "<add key=\"BlogEngine.GalleryFeedUrl\" value=\"http://localhost/v01/nuget\" />";

            ReplaceInFile(_root + "\\be\\App_Data\\settings.xml", comm, appSet);
            ReplaceInFile(_root + "\\bedb\\App_Data\\settings.xml", comm, appSet);
            ReplaceInFile(_root + "\\blog\\App_Data\\settings.xml", comm, appSet);
            Console.WriteLine("5 - done pointing web.configs to local feed v01");
            
            // 6. refresh new release in gallery feed
            if (File.Exists(_root + "\\v01\\Releases\\" + _ver + ".zip"))
            {
                File.Delete(_root + "\\v01\\Releases\\" + _ver + ".zip");
            }
            ZipFolder(_root + "\\v01\\Releases\\" + _ver + ".zip", _root + "\\be");
            Console.WriteLine("6 - done refreshing new release/zip");

            Console.WriteLine("All done!");
            Console.Read();
        }

        static void RunDDL()
        {        
            try
            {
                SqlConnection conn = new SqlConnection(_conn);
                Server server = new Server(new ServerConnection(conn));

                FileInfo file = new FileInfo(AppDomain.CurrentDomain.BaseDirectory + "\\dbreset.sql");
                string script = file.OpenText().ReadToEnd();
                server.ConnectionContext.ExecuteNonQuery(script);
                //--------------------------
                FileInfo file2 = new FileInfo(_root + "\\be\\setup\\SQLServer\\Setup.sql");
                string script2 = file2.OpenText().ReadToEnd();
                server.ConnectionContext.ExecuteNonQuery(script2);

                Console.WriteLine("3 - done running DDL scripts");
            }
            catch (Exception ex)
            {
                Console.WriteLine("3 - failed: " + ex.Message);
            }
        }

        static void ForceDeleteDirectory(string path)
        {
            DirectoryInfo fol;
            var fols = new Stack<DirectoryInfo>();
            var root = new DirectoryInfo(path);
            fols.Push(root);
            while (fols.Count > 0)
            {
                fol = fols.Pop();
                fol.Attributes = fol.Attributes & ~(FileAttributes.Archive | FileAttributes.ReadOnly | FileAttributes.Hidden);
                foreach (DirectoryInfo d in fol.GetDirectories())
                {
                    fols.Push(d);
                }
                foreach (FileInfo f in fol.GetFiles())
                {
                    f.Attributes = f.Attributes & ~(FileAttributes.Archive | FileAttributes.ReadOnly | FileAttributes.Hidden);
                    f.Delete();
                }
            }
            root.Delete(true);
        }

        public static void CopyDir(DirectoryInfo source, DirectoryInfo target)
        {
            foreach (DirectoryInfo dir in source.GetDirectories())
                CopyDir(dir, target.CreateSubdirectory(dir.Name));
            foreach (FileInfo file in source.GetFiles())
                file.CopyTo(Path.Combine(target.FullName, file.Name));
        }

        static void ReplaceInFile(string filePath, string searchText, string replaceText)
        {
            var cnt = 0;
            StreamReader reader = new StreamReader(filePath);
            string content = reader.ReadToEnd();
            cnt = content.Length;
            reader.Close();

            content = Regex.Replace(content, searchText, replaceText);

            StreamWriter writer = new StreamWriter(filePath);
            writer.Write(content);
            writer.Close();
        }

        public static void ZipFolder(string outPathname, string folderName)
        {
            FileStream fsOut = File.Create(outPathname);
            ZipOutputStream zipStream = new ZipOutputStream(fsOut);
            zipStream.SetLevel(3);
            CompressFolder(folderName, zipStream);
            zipStream.IsStreamOwner = true;
            zipStream.Close();
        }

        private static void CompressFolder(string path, ZipOutputStream zipStream)
        {
            string[] files = Directory.GetFiles(path);
            foreach (string filename in files)
            {
                FileInfo fi = new FileInfo(filename);

                int offset = _root.Length + 3;
                string entryName = filename.Substring(offset);
                entryName = ZipEntry.CleanName(entryName);
                ZipEntry newEntry = new ZipEntry(entryName);
                newEntry.DateTime = fi.LastWriteTime;

                newEntry.Size = fi.Length;
                zipStream.PutNextEntry(newEntry);

                byte[] buffer = new byte[4096];
                using (FileStream streamReader = File.OpenRead(filename))
                {
                    StreamUtils.Copy(streamReader, zipStream, buffer);
                }
                zipStream.CloseEntry();
            }
            string[] folders = Directory.GetDirectories(path);
            foreach (string folder in folders)
            {
                CompressFolder(folder, zipStream);
            }
        }
    }
}
