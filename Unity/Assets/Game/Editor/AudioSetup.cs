// AudioSetup.cs — проверка, что все файлы SoundBank лежат в Resources/Audio. Импорт-настройки не трогает.
// Запуск: VoxGame → Check Audio (или batch -executeMethod VoxGame.Editor.AudioSetup.Check)
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VoxGame.Editor
{
    public static class AudioSetup
    {
        public const string AudioDir = "Assets/Game/Resources/Audio";

        [MenuItem("VoxGame/Check Audio")]
        public static void Check()
        {
            var missing = FindMissing(out int total);
            string line = "GAME_AUDIO missing=" + missing.Count + " total=" + total
                          + (missing.Count > 0 ? " [" + string.Join(",", missing) + "]" : "");
            if (missing.Count > 0) Debug.LogError(line);
            else Debug.Log(line);
        }

        // Имена ресурсов банка, для которых нет файла .wav в AudioDir.
        public static List<string> FindMissing(out int total)
        {
            var missing = new List<string>();
            total = 0;
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), AudioDir);
            foreach (var def in SoundBank.Defs)
            {
                foreach (var name in def.files)
                {
                    total++;
                    if (!File.Exists(Path.Combine(dir, name + ".wav"))) missing.Add(name);
                }
            }
            return missing;
        }
    }
}
