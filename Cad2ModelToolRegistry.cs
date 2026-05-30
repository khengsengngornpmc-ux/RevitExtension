using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class Cad2ModelToolRegistry
    {
        public string Version { get; set; } = "1.0";
        public string Source { get; set; } = "Built-in TAS Identify registry";
        public List<Cad2ModelToolCategory> Categories { get; set; } = new List<Cad2ModelToolCategory>();

        public static Cad2ModelToolRegistry Load()
        {
            foreach (string path in GetCandidateConfigPaths())
            {
                Cad2ModelToolRegistry registry = TryLoadFromFile(path);
                if (registry != null)
                {
                    return registry;
                }
            }

            return CreateDefault();
        }

        public Cad2ModelToolCategory FindCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return null;
            }

            return (Categories ?? new List<Cad2ModelToolCategory>())
                .FirstOrDefault(category => string.Equals(category.Name, categoryName, StringComparison.OrdinalIgnoreCase));
        }

        public Cad2ModelToolItem FindFirstToolItem(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return null;
            }

            foreach (Cad2ModelToolCategory category in Categories ?? new List<Cad2ModelToolCategory>())
            {
                foreach (Cad2ModelToolItem item in category.Items ?? new List<Cad2ModelToolItem>())
                {
                    if (string.Equals(item.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        public string GetCategoryForTool(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return "";
            }

            foreach (Cad2ModelToolCategory category in Categories ?? new List<Cad2ModelToolCategory>())
            {
                bool containsTool = (category.Items ?? new List<Cad2ModelToolItem>())
                    .Any(item => string.Equals(item.ToolName, toolName, StringComparison.OrdinalIgnoreCase));

                if (containsTool)
                {
                    return category.Name ?? "";
                }
            }

            return "Others";
        }

        public string GetLabelForTool(string toolName)
        {
            Cad2ModelToolItem item = FindFirstToolItem(toolName);
            if (item != null && !string.IsNullOrWhiteSpace(item.Label))
            {
                return item.Label;
            }

            return string.IsNullOrWhiteSpace(toolName) ? "Not Connected" : toolName;
        }

        private static Cad2ModelToolRegistry TryLoadFromFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                Cad2ModelToolRegistry registry = CamboBimJson.Deserialize<Cad2ModelToolRegistry>(json);
                return Normalize(registry, path);
            }
            catch
            {
                return null;
            }
        }

        private static Cad2ModelToolRegistry Normalize(Cad2ModelToolRegistry registry, string sourcePath)
        {
            if (registry == null || registry.Categories == null || registry.Categories.Count == 0)
            {
                return null;
            }

            registry.Source = string.IsNullOrWhiteSpace(sourcePath) ? registry.Source : sourcePath;
            registry.Categories = registry.Categories
                .Where(category => category != null && !string.IsNullOrWhiteSpace(category.Name))
                .Select(category =>
                {
                    category.Items = (category.Items ?? new List<Cad2ModelToolItem>())
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Label))
                        .ToList();
                    return category;
                })
                .Where(category => category.Items.Count > 0)
                .ToList();

            return registry.Categories.Count == 0 ? null : registry;
        }

        private static IEnumerable<string> GetCandidateConfigPaths()
        {
            var folders = new List<string>
            {
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory()
            };

            foreach (string folder in folders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(folder, "Config", "cad2model-tool-registry.json");
            }
        }

        private static Cad2ModelToolRegistry CreateDefault()
        {
            return new Cad2ModelToolRegistry
            {
                Categories = new List<Cad2ModelToolCategory>
                {
                    Category("Axis", Tool("Axis Grid(X)", "Gridline"), Future("Secondary Axis(U)", "Secondary Axis")),
                    Category("Segmentation", Future("Segmentation(SE)", "Segmentation")),
                    Category("Column", Tool("Column(C)", "Column"), Future("Stiffener(S)", "Stiffener"), Future("Corbel(E)", "Corbel")),
                    Category("Wall", Tool("Wall(W)", "Wall"), Future("Curtain Wall(W)", "Curtain Wall"), Future("Vertical Projection(P)", "Vertical Projection"), Future("Horizontal Projection(P)", "Horizontal Projection")),
                    Category("Door/Window Opening", Future("Door(D)", "Door"), Future("Window(D)", "Window"), Future("DoorWin(D)", "DoorWin"), Future("Wall Opening(O)", "Wall Opening"), Future("Ribbon Window(O)", "Ribbon Window"), Future("Ribbon Opening(O)", "Ribbon Opening"), Future("Bay Window(Y)", "Bay Window"), Future("Dormer(M)", "Dormer"), Future("Lintel(L)", "Lintel"), Future("Niche(N)", "Niche"), Future("Skylight(F)", "Skylight")),
                    Category("Beam", Tool("Beam(B)", "Beam"), Future("Coupling Beam(J)", "Coupling Beam"), Future("Ring Beam(K)", "Ring Beam")),
                    Category("Slab", Tool("In-situ Slab(S)", "Slab"), Future("Precast Slab(S)", "Precast Slab"), Future("Spiral Slab(S)", "Spiral Slab"), Future("Ramp(R)", "Ramp"), Future("Drop Panel(V)", "Drop Panel"), Future("Slab Opening(N)", "Slab Opening")),
                    Category("Steel Structure", Future("Steel/Composite Column", "Steel/Composite Column"), Future("Steel/Composite Beam", "Steel/Composite Beam"), Future("Steel/Composite Slab(S)", "Steel/Composite Slab"), Future("Steel Column(UC)", "Steel Column"), Future("Steel Beam(UB)", "Steel Beam"), Future("Plate(P)", "Plate")),
                    Category("Staircase", Future("Staircase(F)", "Staircase"), Future("Straight Flight(F)", "Straight Flight"), Future("Spiral Flight(F)", "Spiral Flight"), Future("Stairwell(F)", "Stairwell")),
                    Category("Finishes", Future("Room(R)", "Room"), Future("Floor Finish(F)", "Floor Finish"), Future("Waterproof(P)", "Waterproof"), Future("Skirting(T)", "Skirting"), Future("Wall Finish(U)", "Wall Finish"), Future("Ceiling Finish(E)", "Ceiling Finish"), Future("Suspended Ceiling(H)", "Suspended Ceiling")),
                    Category("Prefabrication", Future("Component(CC)", "Component"), Future("PPVC(PP)", "PPVC"), Future("Unit Type(UU)", "Unit Type")),
                    Category("Foundation", Tool("Pile Cap(P)", "Foundation"), Tool("Pile(P)", "Bored Pile"), Future("Ground Beam(F)", "Ground Beam"), Tool("Raft Foundation(M)", "Foundation"), Future("Sump Pit(S)", "Sump Pit"), Tool("Pad Foundation(I)", "Foundation"), Tool("Strip Foundation(N)", "Foundation"), Tool("Blinding(X)", "Lean Concrete")),
                    Category("Excavation", Tool("Heavy Excavation(H)", "Soil Excavation"), Tool("Trench Excavation(T)", "Soil Excavation"), Tool("Pit Excavation(D)", "Soil Excavation"), Future("Room Backfill(B)", "Room Backfill")),
                    Category("Others", Future("Floor Area(U)", "Floor Area"), Future("Courtyard(C)", "Courtyard"), Future("Site Leveling(G)", "Site Leveling"), Future("Apron(A)", "Apron"), Future("Steps(F)", "Steps"), Future("Post Cast Strip(L)", "Post Cast Strip"), Future("Eave(E)", "Eave"), Future("Canopy(P)", "Canopy"), Future("Balcony(Y)", "Balcony"), Future("Roof(M)", "Roof"), Future("Kerb(P)", "Kerb"), Future("Coping(T)", "Coping"), Future("Railing(R)", "Railing")),
                    Category("Custom Element", Future("Custom Point(P)", "Custom Point"), Future("Custom Line(L)", "Custom Line"), Future("Custom Area(A)", "Custom Area")),
                    Category("Custom Quantity", Future("Custom Quantity(Q)", "Custom Quantity"))
                }
            };
        }

        private static Cad2ModelToolCategory Category(string name, params Cad2ModelToolItem[] items)
        {
            return new Cad2ModelToolCategory
            {
                Name = name,
                IsExpanded = true,
                Items = items.ToList()
            };
        }

        private static Cad2ModelToolItem Tool(string label, string toolName)
        {
            return new Cad2ModelToolItem
            {
                Label = label,
                ToolName = toolName,
                Key = toolName,
                Status = "Connected"
            };
        }

        private static Cad2ModelToolItem Future(string label, string key)
        {
            return new Cad2ModelToolItem
            {
                Label = label,
                ToolName = "",
                Key = key,
                Status = "Future"
            };
        }
    }

    internal sealed class Cad2ModelToolCategory
    {
        public string Name { get; set; } = "";
        public bool IsExpanded { get; set; }
        public List<Cad2ModelToolItem> Items { get; set; } = new List<Cad2ModelToolItem>();
    }

    internal sealed class Cad2ModelToolItem
    {
        public string Label { get; set; } = "";
        public string Key { get; set; } = "";
        public string ToolName { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
