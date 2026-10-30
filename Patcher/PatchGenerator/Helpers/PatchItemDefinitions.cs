using Avalonia.Media;
using System.Collections.Generic;

namespace PatchGenerator.Helpers
{
    public static class PatchItemDefinitions
    {
        public static Dictionary<string, IBrush> Colors = new Dictionary<string, IBrush>()
        {
            {"delta", new SolidColorBrush(Color.Parse("#A684FF")) },
            {"new", new SolidColorBrush(Color.Parse("#05DF72")) },
            {"del", new SolidColorBrush(Color.Parse("#FF6467")) },
            {"exists", new SolidColorBrush(Color.Parse("#6A7282")) }
        };
    }
}
