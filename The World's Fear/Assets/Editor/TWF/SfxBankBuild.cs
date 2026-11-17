using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TWF.EditorTools
{
    // Collects Assets/Sounds/<Event>/Sfx_<Event>_NN.wav into Resources/TWF/SfxBank.asset.
    public static class SfxBankBuild
    {
        const string Root = "Assets/Sounds";
        const string Output = "Assets/Resources/TWF/SfxBank.asset";

        [MenuItem("TWF/Build Sfx Bank")]
        public static string Run()
        {
            var entries = new List<SfxBank.Entry>();
            int total = 0;
            foreach (var dir in Directory.GetDirectories(Root).OrderBy(d => d))
            {
                string key = Path.GetFileName(dir);
                var paths = Directory.GetFiles(dir, "Sfx_" + key + "_*.wav").Select(p => p.Replace((char)92, '/')).OrderBy(p => p).ToArray();
                if (paths.Length == 0) continue;
                var clips = new List<AudioClip>();
                foreach (var p in paths)
                {
                    var imp = (AudioImporter)AssetImporter.GetAtPath(p);
                    if (imp == null) continue;
                    var s = imp.defaultSampleSettings;
                    // Played positionally and often: mono, compressed, decoded once when loaded.
                    if (!imp.forceToMono || s.compressionFormat != AudioCompressionFormat.Vorbis || s.loadType != AudioClipLoadType.DecompressOnLoad)
                    {
                        imp.forceToMono = true;
                        s.compressionFormat = AudioCompressionFormat.Vorbis;
                        s.quality = 0.6f;
                        s.loadType = AudioClipLoadType.DecompressOnLoad;
                        imp.defaultSampleSettings = s;
                        imp.SaveAndReimport();
                    }
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                    if (clip != null) clips.Add(clip);
                }
                entries.Add(new SfxBank.Entry { key = key, clips = clips.ToArray() });
                total += clips.Count;
            }
            var bank = AssetDatabase.LoadAssetAtPath<SfxBank>(Output);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<SfxBank>();
                AssetDatabase.CreateAsset(bank, Output);
            }
            bank.entries = entries.ToArray();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            return entries.Count + " events, " + total + " clips";
        }
    }
}
