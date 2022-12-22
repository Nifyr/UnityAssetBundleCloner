using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SmartPoint.AssetAssistant;
using System.Text.RegularExpressions;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace UnityAssetBundleCloner
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            string outputDir = Environment.CurrentDirectory + "\\Output";
            string tempDir = Environment.CurrentDirectory + "\\Temp";
            Dictionary<string, string> cabNames = new();
            Dictionary<string, string> assetBundleNames = new();
            Random rng = new();

            OpenFileDialog aaofd = new()
            {
                Filter = "AssetAssistant Objects(*.bin)|*.bin",
                RestoreDirectory = true
            };
            if (aaofd.ShowDialog() != DialogResult.OK) return;
            string assetAssistantPath = aaofd.FileName;
            string assetAssistantDir = Path.GetDirectoryName(assetAssistantPath) ?? throw new ArgumentException("Not a path.");

            OpenFileDialog abofd = new()
            {
                Filter = "AssetBundle(*.*)|*.*",
                RestoreDirectory = true
            };
            if (abofd.ShowDialog() != DialogResult.OK) return;
            string mainAssetBundlePath = abofd.FileName;
            string mainAssetBundleDir = mainAssetBundlePath.Split("AssetAssistant\\").Last();
            string projectName = mainAssetBundleDir.Split('\\').First();
            string mainAssetBundleName = mainAssetBundleDir[(projectName.Length + 1)..];

            AssetBundleDownloadManifest abdm = AssetBundleDownloadManifest.Load(assetAssistantPath);
            HashSet<string> assetPaths = abdm.records.SelectMany(abr => abr.assetPaths).ToHashSet();
            List<AssetBundleRecord> abrs = abdm.GetAssetBundleRecordsWithDependencies(mainAssetBundleName.Replace('\\', '/'), true).Where(abr => abr.projectName == projectName).ToList();
            abrs.Sort((abr0, abr1) => abr0.allDependencies.Length.CompareTo(abr1.allDependencies.Length));
            AssetsManager am = new();
            BundleFileInstance[] bfis = abrs.Select(abr => am.LoadBundleFile(assetAssistantDir + "\\" + projectName + "\\" + abr.assetBundleName)).ToArray();
            for (int i = 0; i < abrs.Count; i++)
            {
                AssetBundleRecord abr = abrs[i];
                BundleFileInstance bfi = bfis[i];
                DecompressBundle(bfi);
                AssetsFileInstance afi = am.LoadAssetsFileFromBundle(bfi, 0);
                cabNames[afi.name.Replace("CAB-", "")] = GenCABName(rng);
                string newAssetBundleName = abr.assetBundleName;
                do
                {
                    TextInputForm tip = new("AssetBundle Name", "Input new assetbundle name:", abr.assetBundleName);
                    tip.ShowDialog();
                    newAssetBundleName = tip.OutString;
                    if (newAssetBundleName == abr.assetBundleName)
                        MessageBox.Show("Name cannot be the same as the old one.\nInput another assetbundle name.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                } while (newAssetBundleName == abr.assetBundleName);
                assetBundleNames[abr.assetBundleName] = newAssetBundleName;
            }

            for (int i = 0; i < abrs.Count; i++)
            {
                AssetBundleRecord abr = abrs[i];
                AssetBundleRecord newRecord = (AssetBundleRecord)abr.Clone();
                newRecord.assetBundleName = assetBundleNames[newRecord.assetBundleName];

                for (int j = 0; j < newRecord.assetPaths.Length; j++)
                    while (assetPaths.Contains(newRecord.assetPaths[j]))
                        newRecord.assetPaths[j] = IncrementName(newRecord.assetPaths[j]);

                for (int j = 0; j < newRecord.allDependencies.Length; j++)
                    if (assetBundleNames.ContainsKey(newRecord.allDependencies[j]))
                        newRecord.allDependencies[j] = assetBundleNames[newRecord.allDependencies[j]];

                abdm.Add(newRecord);

                BundleFileInstance bfi = bfis[i];
                List<AssetsReplacer> ars = new();
                AssetsFileInstance afi = am.LoadAssetsFileFromBundle(bfi, 0);
                string oldCAB = afi.name.Replace("CAB-", "");
                afi.name = afi.name.Replace(oldCAB, cabNames[oldCAB]);
                foreach (AssetBundleDirectoryInfo06 abdi6 in bfi.file.bundleInf6.dirInf)
                    abdi6.name = abdi6.name.Replace(oldCAB, cabNames[oldCAB]);
                foreach (AssetsFileDependency afd in afi.file.dependencies.dependencies)
                {
                    string cab = afd.assetPath.Replace("CAB-", "");
                    if (cabNames.ContainsKey(cab))
                        afd.assetPath = afd.assetPath.Replace(cab, cabNames[cab]);
                }

                List<AssetTypeValueField> texture2Ds = afi.table.GetAssetsOfType((int)AssetClassID.Texture2D).Select(afie => am.GetTypeInstance(afi, afie).GetBaseField()).ToList();
                foreach (AssetTypeValueField texture2DField in texture2Ds)
                {
                    string m_Name = texture2DField["m_Name"].value.AsString();
                    AssetFileInfoEx afie = afi.table.GetAssetInfo(m_Name, (int)AssetClassID.Texture2D);

                    string m_StreamDataPath = texture2DField["m_StreamData"].children[2].value.AsString();
                    m_StreamDataPath = m_StreamDataPath.Replace(oldCAB, cabNames[oldCAB]);
                    texture2DField["m_StreamData"].children[2].GetValue().Set(m_StreamDataPath);

                    byte[] b = texture2DField.WriteToByteArray();
                    AssetsReplacerFromMemory arfm = new(0, afie.index, (int)afie.curFileType, AssetHelper.GetScriptIndex(afi.file, afie), b);
                    ars.Add(arfm);
                }

                am.UpdateDependencies(afi);

                MemoryStream memoryStream = new MemoryStream();
                AssetsFileWriter afw = new(memoryStream);
                afi.file.dependencies.Write(afw);
                afi.file.Write(afw, 0, ars, 0);
                BundleReplacerFromMemory brfm = new(bfi.file.bundleInf6.dirInf[0].name, "CAB-" + cabNames[oldCAB], true, memoryStream.ToArray(), -1);

                string tempAssetBundlePath = tempDir + "\\" + newRecord.projectName + "\\" + newRecord.assetBundleName;
                string outputAssetBundlePath = outputDir + "\\" + newRecord.projectName + "\\" + newRecord.assetBundleName;
                Directory.CreateDirectory(Path.GetDirectoryName(tempAssetBundlePath) ?? throw new ArgumentException("Not a path."));
                Directory.CreateDirectory(Path.GetDirectoryName(outputAssetBundlePath) ?? throw new ArgumentException("Not a path."));
                afw = new(File.OpenWrite(tempAssetBundlePath));
                bfi.file.Write(afw, new List<BundleReplacer> { brfm });
                afw.Close();
                am = new();
                bfi = am.LoadBundleFile(tempAssetBundlePath, false);
                if (File.Exists(outputAssetBundlePath))
                    File.Delete(outputAssetBundlePath);
                FileStream stream = File.OpenWrite(outputAssetBundlePath);
                afw = new AssetsFileWriter(stream);
                bfi.file.Pack(bfi.file.reader, afw, AssetBundleCompressionType.LZ4);
                afw.Close();
                bfi.file.Close();
                bfi.BundleStream.Dispose();
                File.Delete(tempAssetBundlePath);
            }

            abdm.Save(outputDir + "\\" + Path.GetFileName(assetAssistantPath));
            MessageBox.Show("Result placed in Output folder.", "Success!", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string IncrementName(string s)
        {
            Match m = Regex.Match(s, @"\d+\z");
            if (!m.Success)
                return s + "0";
            return s[..(s.Length - m.Value.Length)] + (int.Parse(m.Value) + 1);
        }

        private static void DecompressBundle(BundleFileInstance bfi)
        {
            AssetBundleFile abf = bfi.file;

            MemoryStream stream = new();
            abf.Unpack(abf.reader, new AssetsFileWriter(stream));

            stream.Position = 0;

            AssetBundleFile newAbf = new();
            newAbf.Read(new AssetsFileReader(stream), false);

            abf.reader.Close();
            bfi.file = newAbf;
        }

        private static string GenCABName(Random rng)
        {
            string[] values = new string[4];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = rng.Next().ToString("x8");
            }

            return string.Join("", values);
        }
    }
}