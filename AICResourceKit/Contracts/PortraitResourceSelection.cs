using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AICResourceKit.Contracts
{
    /// <summary>主界面立绘的显示条件；姿态和状态来自游戏实际选择，不改变游戏状态。</summary>
    public sealed class PortraitResourceSelection
    {
        private static readonly Dictionary<string, uint> States = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["NORMAL"] = 0, ["DIRT"] = 1, ["PROG0"] = 2, ["PROG1"] = 4, ["PROG2"] = 8,
            ["LOWHP"] = 32, ["BATTLE"] = 64, ["SER"] = 128, ["SHAMED"] = 256,
            ["SMASH"] = 512, ["ABSORBED"] = 1024, ["STUNNED"] = 4096, ["LOWMP"] = 8192,
            ["EGGED"] = 16384, ["WET"] = 32768, ["BOTE"] = 65536, ["ORGASM"] = 131072,
            ["CONFUSED"] = 262144, ["OSGM"] = 524288, ["TORNED"] = 1048576,
            ["SLEEP"] = 2097152, ["DEAD"] = 4194304, ["STONEOVER"] = 8388608, ["SP_SENSITIVE"] = 1073741824
        };

        public IReadOnlyList<string> Poses { get; private set; }
        public IReadOnlyList<string> Animations { get; private set; }
        public IReadOnlyList<string> RequireStates { get; private set; }
        public IReadOnlyList<string> ExcludeStates { get; private set; }

        public static PortraitResourceSelection Parse(Dictionary<string, object> json, bool spine)
        {
            if (json.Keys.Any(key => key != "poses" && key != "animations" && key != "requireStates" && key != "excludeStates"))
                throw new InvalidDataException("Unknown portraitSelection field.");
            var result = new PortraitResourceSelection
            {
                Poses = Names(json, "poses"), Animations = Names(json, "animations"),
                RequireStates = Names(json, "requireStates"), ExcludeStates = Names(json, "excludeStates")
            };
            if (!spine && result.Animations.Count != 0)
                throw new InvalidDataException("portraitSelection.animations applies to portrait Spine only.");
            if (result.Poses.Count + result.Animations.Count + result.RequireStates.Count + result.ExcludeStates.Count == 0)
                throw new InvalidDataException("portraitSelection must contain at least one condition.");
            foreach (string state in result.RequireStates.Concat(result.ExcludeStates))
                if (!States.ContainsKey(state)) throw new InvalidDataException("Unknown portrait state: " + state);
            if (result.RequireStates.Intersect(result.ExcludeStates).Any()
                || (result.RequireStates.Contains("NORMAL") && result.RequireStates.Count > 1))
                throw new InvalidDataException("Contradictory portrait state conditions.");
            return result;
        }

        public bool Matches(string pose, uint state, string animation = null) =>
            (Poses.Count == 0 || Poses.Contains(pose))
            && (Animations.Count == 0 || Animations.Contains(animation))
            && RequireStates.All(name => HasState(state, name))
            && !ExcludeStates.Any(name => HasState(state, name));

        private static bool HasState(uint value, string name) => States[name] == 0 ? value == 0 : (value & States[name]) != 0;

        private static string[] Names(Dictionary<string, object> json, string key)
        {
            if (!json.TryGetValue(key, out object raw)) return new string[0];
            var values = ContractValue.Array(raw).Select(value => value as string).ToArray();
            if (values.Length == 0 || values.Any(value => string.IsNullOrWhiteSpace(value)
                || value.Trim() != value || value.Contains("\n") || value.Contains("\r"))
                || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new InvalidDataException("Expected distinct nonempty names: portraitSelection." + key);
            return values;
        }
    }
}
