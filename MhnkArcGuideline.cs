using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace CamboBIM.Revit2024.Addin
{
    internal static class MhnkArcGuideline
    {
        private static readonly string[] CategoryOrder = { "Filter", "Creation", "Edition", "Solids", "Xpress" };
        private static readonly Dictionary<string, ToolGuide> SpecificGuides = CreateSpecificGuides();

        public static void Open(IList<MhnkArcCommandOption> options, MhnkArcCommandOption selectedOption)
        {
            string path = WriteHtml(options ?? new List<MhnkArcCommandOption>(), selectedOption);
            string anchor = selectedOption == null ? "top" : GetAnchor(selectedOption);
            string url = new Uri(path).AbsoluteUri + "#" + anchor;

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private static string WriteHtml(IList<MhnkArcCommandOption> options, MhnkArcCommandOption selectedOption)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "Guidelines");
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, "MHNK_ARC_Tools_Guideline.html");
            File.WriteAllText(path, BuildHtml(options, selectedOption), Encoding.UTF8);
            return path;
        }

        private static string BuildHtml(IList<MhnkArcCommandOption> options, MhnkArcCommandOption selectedOption)
        {
            var orderedOptions = (options ?? new List<MhnkArcCommandOption>())
                .Where(x => x != null)
                .OrderBy(x => GetCategoryOrder(x.Category))
                .ThenBy(x => x.Title)
                .ToList();

            int specificCount = orderedOptions.Count(x => SpecificGuides.ContainsKey(GetKey(x)));

            var html = new StringBuilder();
            html.AppendLine("<!doctype html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"utf-8\">");
            html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            html.AppendLine("<title>MHNK ARC Tools Full Guideline Set</title>");
            html.AppendLine("<style>");
            html.AppendLine("body{margin:0;font-family:Segoe UI,Arial,sans-serif;background:#f5f7fb;color:#1f2937;line-height:1.55}");
            html.AppendLine("header{background:#172033;color:#fff;padding:26px 32px;border-bottom:4px solid #2f80ed}");
            html.AppendLine("header h1{margin:0 0 6px 0;font-size:28px;font-weight:650}");
            html.AppendLine("header p{margin:0;color:#cbd5e1}");
            html.AppendLine("main{max-width:1240px;margin:0 auto;padding:24px}");
            html.AppendLine(".notice,.tool{background:#fff;border:1px solid #d9e2ec;border-radius:8px;padding:18px;margin:0 0 16px 0;box-shadow:0 1px 2px rgba(15,23,42,.05)}");
            html.AppendLine(".notice{border-left:5px solid #2f80ed}");
            html.AppendLine(".toc{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:12px;margin:18px 0}");
            html.AppendLine(".toc section{background:#fff;border:1px solid #d9e2ec;border-radius:8px;padding:14px}");
            html.AppendLine(".toc h2{font-size:17px;margin:0 0 8px 0}");
            html.AppendLine(".toc a{display:block;color:#1458b3;text-decoration:none;margin:5px 0;font-size:14px}");
            html.AppendLine(".toc a:hover{text-decoration:underline}");
            html.AppendLine(".category{margin:28px 0 12px 0;font-size:24px;color:#111827}");
            html.AppendLine(".tool h3{margin:0 0 6px 0;font-size:20px}");
            html.AppendLine(".meta{display:flex;flex-wrap:wrap;gap:8px;margin:8px 0 12px 0}");
            html.AppendLine(".pill{background:#eef4ff;color:#174ea6;border:1px solid #c8dcff;border-radius:999px;padding:3px 10px;font-size:12px;font-weight:600}");
            html.AppendLine(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:12px}");
            html.AppendLine(".remote{background:#f8fbff;border:1px solid #bfd7ff;border-left:5px solid #2f80ed;border-radius:8px;padding:14px;margin:12px 0}");
            html.AppendLine(".remote h4{margin:0 0 8px 0;color:#0f172a;font-size:16px}");
            html.AppendLine(".remote ol{margin:6px 0 0 22px;padding:0}");
            html.AppendLine(".remote li{margin:5px 0}");
            html.AppendLine(".box{background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;padding:12px}");
            html.AppendLine(".box h4{margin:0 0 6px 0;font-size:14px;color:#0f172a}");
            html.AppendLine("ul,ol{margin:6px 0 0 22px;padding:0}");
            html.AppendLine("li{margin:4px 0}");
            html.AppendLine(".selected{border-color:#2f80ed;box-shadow:0 0 0 2px rgba(47,128,237,.16)}");
            html.AppendLine("footer{color:#64748b;font-size:12px;margin:24px 0}");
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("<header id=\"top\">");
            html.AppendLine("<h1>MHNK ARC Tools Full Guideline Set</h1>");
            html.AppendLine("<p>Opened command: " + Encode(selectedOption == null ? "All tools" : selectedOption.Category + " / " + selectedOption.Title) + "</p>");
            html.AppendLine("</header>");
            html.AppendLine("<main>");
            html.AppendLine("<div class=\"notice\">");
            html.AppendLine("<strong>Full set coverage:</strong> " + specificCount.ToString() + " / " + orderedOptions.Count.ToString() + " current ARC tools have dedicated guidelines. Save the model, confirm the active view, run previews carefully, and inspect created or modified elements before continuing.");
            html.AppendLine("</div>");
            AppendRemoteSessionChecklist(html, selectedOption);
            html.AppendLine("<nav class=\"toc\">");
            foreach (var group in orderedOptions.GroupBy(x => x.Category))
            {
                html.AppendLine("<section>");
                html.AppendLine("<h2>" + Encode(group.Key) + "</h2>");
                foreach (MhnkArcCommandOption option in group)
                {
                    html.AppendLine("<a href=\"#" + GetAnchor(option) + "\">" + Encode(option.Title) + "</a>");
                }
                html.AppendLine("</section>");
            }
            html.AppendLine("</nav>");

            foreach (var group in orderedOptions.GroupBy(x => x.Category))
            {
                html.AppendLine("<h2 class=\"category\">" + Encode(group.Key) + "</h2>");
                foreach (MhnkArcCommandOption option in group)
                {
                    ToolGuide guide = GetGuide(option);
                    bool isSelected = selectedOption != null &&
                                      string.Equals(option.Category, selectedOption.Category, StringComparison.OrdinalIgnoreCase) &&
                                      string.Equals(option.Title, selectedOption.Title, StringComparison.OrdinalIgnoreCase);
                    html.AppendLine("<article id=\"" + GetAnchor(option) + "\" class=\"tool" + (isSelected ? " selected" : "") + "\">");
                    html.AppendLine("<h3>" + Encode(option.Title) + "</h3>");
                    html.AppendLine("<div class=\"meta\"><span class=\"pill\">" + Encode(option.Metadata?.SourceGroup ?? "") + "</span><span class=\"pill\">" + Encode(option.Category) + "</span><span class=\"pill\">Status: " + Encode(option.Status) + "</span><span class=\"pill\">Risk: " + Encode(option.Metadata?.Risk ?? "") + "</span><span class=\"pill\">" + Encode(guide.UseWhen) + "</span></div>");
                    html.AppendLine("<p>" + Encode(option.Summary) + "</p>");
                    AppendToolGuideline(html, option, guide);
                    html.AppendLine("</article>");
                }
            }

            html.AppendLine("<footer>MHNK ARC Tools Full Guideline Set - generated " + Encode(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "</footer>");
            html.AppendLine("</main>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            return html.ToString();
        }

        private static void AppendRemoteSessionChecklist(StringBuilder html, MhnkArcCommandOption selectedOption)
        {
            html.AppendLine("<section class=\"remote\">");
            html.AppendLine("<h4>Remote Session Checklist for Human Tester</h4>");
            html.AppendLine("<ol>");
            foreach (string line in BuildRemoteSessionLines(selectedOption))
            {
                html.AppendLine("<li>" + Encode(line) + "</li>");
            }
            html.AppendLine("</ol>");
            html.AppendLine("</section>");
        }

        private static IList<string> BuildRemoteSessionLines(MhnkArcCommandOption selectedOption)
        {
            var lines = new List<string>
            {
                "Ask the human tester to work in a copy, detached model, or approved test file before any Creation, Edition, Solids, cleanup, delete, or batch command.",
                "Ask them to open the correct Revit view first: plan view for room/CAD/door/window work, 3D coordination view for solids/clash work, or the target documentation view for graphics/filter work.",
                "Tell them to open MHNK > ARC TOOLS, choose the correct group, then choose the exact tool name shown in this guideline.",
                "Tell them to choose the source mode in SELECTED ELEMENTS: Select Item for manual selection, Category for visible category scope, By Layer for CAD layer workflows, or All only for approved model-wide checks.",
                "Ask them to confirm the rows in SELECTED ELEMENTS and TARGET/RESULT before clicking PREVIEW / RUN.",
                "After running, ask for evidence: screenshot of the tool panel, preview/options window if shown, result message, active model view after the command, and any created/exported report path.",
                "If the result is wrong, ask them to stop, use Revit Undo immediately, and report the selected source mode, active view name, selected element count, and exact message shown."
            };

            if (selectedOption != null)
            {
                lines.Insert(3, "Current opened tool: " + selectedOption.Category + " > " + selectedOption.Title + ".");
            }

            return lines;
        }

        private static void AppendToolGuideline(StringBuilder html, MhnkArcCommandOption option, ToolGuide guide)
        {
            AppendRemoteToolScript(html, option, guide);
            html.AppendLine("<div class=\"grid\">");
            AppendMetadataBox(html, option);
            AppendBox(html, "Before Running", guide.Before);
            AppendBox(html, "Process", guide.Process);
            AppendBox(html, "Expected Result", guide.Expected);
            AppendBox(html, "QA Check", guide.Qa);
            AppendBox(html, "Caution", guide.Caution);
            html.AppendLine("</div>");
        }

        private static void AppendMetadataBox(StringBuilder html, MhnkArcCommandOption option)
        {
            MhnkArcToolMetadata metadata = option?.Metadata;
            if (metadata == null)
            {
                return;
            }

            AppendBox(html, "Production Panel Contract", new[]
            {
                "Tool ID: " + metadata.ToolId,
                "Panel: " + metadata.PanelKind,
                "Source group: " + metadata.SourceGroup,
                "Supported source modes: " + metadata.SupportedSourceModeText,
                "Live retrieve: " + metadata.LiveRetrieveScope,
                "Preview columns: " + string.Join(", ", metadata.PreviewColumns.ToArray()),
                "Result columns: " + string.Join(", ", metadata.ResultColumns.ToArray())
            });
        }

        private static void AppendRemoteToolScript(StringBuilder html, MhnkArcCommandOption option, ToolGuide guide)
        {
            html.AppendLine("<div class=\"remote\">");
            html.AppendLine("<h4>Remote Human Use Script</h4>");
            html.AppendLine("<ol>");
            foreach (string line in BuildRemoteToolLines(option, guide))
            {
                html.AppendLine("<li>" + Encode(line) + "</li>");
            }
            html.AppendLine("</ol>");
            html.AppendLine("</div>");
        }

        private static IList<string> BuildRemoteToolLines(MhnkArcCommandOption option, ToolGuide guide)
        {
            string category = option?.Category ?? "ARC";
            string title = option?.Title ?? "Selected Tool";
            string sourceMode = GetRecommendedSourceModeLabel(option);

            var lines = new List<string>
            {
                "Say: In Revit, open the MHNK tab, click ARC TOOLS, then click " + category + " in GROUPS and " + title + " in TOOLS.",
                "Say: In SELECTED ELEMENTS, choose " + sourceMode + " unless the test case specifically says to use another source mode.",
                BuildHumanSelectionInstruction(option),
                "Say: Read the rows in SELECTED ELEMENTS out loud. Confirm the active view, selection, category, layer, room, host, or target set is correct.",
                "Say: Read the rows in the right-side result/target panel. Confirm the expected command output before running.",
                "Say: Click PREVIEW or PREVIEW / RUN. If an options or preview window opens, do not accept until the ready rows and type/level/offset values are correct.",
                "Say: After the command finishes, show the model result and the result message. Take a screenshot or note the created/changed/selected/skipped/found count.",
                "Ask: Does the result match this expected outcome? " + FirstLine(guide?.Expected),
                "Ask: Are there unexpected elements changed, hidden, isolated, created, deleted, joined, cut, or overridden? If yes, stop and Undo."
            };

            return lines;
        }

        private static string BuildHumanSelectionInstruction(MhnkArcCommandOption option)
        {
            string text = ((option?.Category ?? "") + " " + (option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
            if (text.Contains("cad") || text.Contains("layer"))
            {
                return "Say: If this is a CAD workflow, select the CAD import, CAD layer source, or CAD/model/detail curves in Revit before running.";
            }

            if (text.Contains("room") || text.Contains("ceiling") || text.Contains("floor") || text.Contains("wall"))
            {
                return "Say: If this is a room or host workflow, select the intended rooms/walls/floors/ceilings, or confirm the active view contains only the intended visible candidates.";
            }

            if (text.Contains("door") || text.Contains("window"))
            {
                return "Say: Select the intended door/window markers and make sure the host walls and family types are available before running.";
            }

            if (text.Contains("join") || text.Contains("cut") || text.Contains("solid") || text.Contains("clash") || text.Contains("opening"))
            {
                return "Say: Select only the solid-capable elements for this test. For first-by-selected tools, select the cutter/reference element first.";
            }

            if (text.Contains("hide") || text.Contains("isolate") || text.Contains("pin") || text.Contains("unpin") || text.Contains("selection"))
            {
                return "Say: Select the exact elements to test in Revit before clicking the command.";
            }

            if (text.Contains("view") || text.Contains("dashboard") || text.Contains("settings") || text.Contains("report") || text.Contains("diagnostics"))
            {
                return "Say: No model selection is required unless the tool panel says otherwise; confirm the active project and view are correct.";
            }

            return "Say: Select the required source elements in Revit, or confirm the active view/source mode is correct for this test.";
        }

        private static string FirstLine(IList<string> lines)
        {
            return (lines ?? new List<string>()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "The command result matches the selected tool purpose.";
        }

        private static string GetRecommendedSourceModeLabel(MhnkArcCommandOption option)
        {
            string text = ((option?.Category ?? "") + " " + (option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
            if (text.Contains("cad") || text.Contains("layer"))
            {
                return "By Layer";
            }

            if (text.Contains("room") || text.Contains("view") || text.Contains("dashboard") || text.Contains("warnings") || text.Contains("missing"))
            {
                return "Category";
            }

            if (text.Contains("all") || text.Contains("delete mhnk") || text.Contains("working plans"))
            {
                return "All";
            }

            return "Select Item";
        }

        private static void AppendBox(StringBuilder html, string title, IList<string> lines)
        {
            html.AppendLine("<div class=\"box\"><h4>" + Encode(title) + "</h4><ul>");
            foreach (string line in lines ?? new List<string>())
            {
                html.AppendLine("<li>" + Encode(line) + "</li>");
            }
            html.AppendLine("</ul></div>");
        }

        private static ToolGuide GetGuide(MhnkArcCommandOption option)
        {
            ToolGuide guide;
            if (SpecificGuides.TryGetValue(GetKey(option), out guide))
            {
                return guide;
            }

            return new ToolGuide(
                "General workflow",
                Lines("Prepare the active view and current selection for this command.", "Save the model before bulk creation or edition."),
                Lines("Open the command from MHNK ARC Tools.", "Review any preview/options window.", "Run only when the ready count matches the intended scope."),
                Lines("The command reports what it created, selected, changed, skipped, or found."),
                Lines("Inspect the active view and current selection immediately after running.", "Undo if the result scope is not correct."),
                Lines("This fallback guide means the command was not in the dedicated catalog."));
        }

        private static Dictionary<string, ToolGuide> CreateSpecificGuides()
        {
            var guides = new Dictionary<string, ToolGuide>(StringComparer.OrdinalIgnoreCase);

            Add(guides, "Filter", "Select by Category", "Fast selection", "Select one seed element.", "Selects visible elements in the active view with the same Revit category as the seed.", "Confirm hidden, template, or linked elements are not expected in the result.");
            Add(guides, "Filter", "Select by Family / Type", "Fast selection", "Select one seed family/type instance.", "Selects visible elements matching the seed type.", "Confirm the selected type is the intended production type before editing.");
            Add(guides, "Filter", "Select by Level", "Level review", "Select a level-based seed element or open the target level view.", "Selects visible elements associated with the selected or active level.", "Check multi-level elements manually.");
            Add(guides, "Filter", "Select by Workset", "Workset review", "Select one seed element from the target workset.", "Selects visible elements on the same workset.", "Verify worksharing is enabled and workset naming is consistent.");
            Add(guides, "Filter", "Select by Phase", "Phase review", "Select one phased element.", "Selects visible elements with the same created phase.", "Check demolished/temporary elements separately if needed.");
            Add(guides, "Filter", "Select by Parameter", "Parameter review", "Select one seed element with the parameter value you want to match.", "Selects visible elements matching a useful seed parameter.", "Review the chosen parameter/value before batch edits.");
            Add(guides, "Filter", "Select by Material", "Material review", "Select one element using the material to find.", "Selects visible elements sharing material with the seed.", "Elements with layered compound structures may need manual confirmation.");
            Add(guides, "Filter", "Select by CAD Layer", "CAD review", "Select one or more CAD imports or open a view containing CAD imports.", "Reports detected CAD layers and selects visible CAD imports.", "Use this before CAD-to-model mapping.");
            Add(guides, "Filter", "Select MEP Elements in ARC View", "Coordination", "Open the ARC coordination view.", "Selects visible MEP coordination categories in the active ARC view.", "Use in ARC only; MEP users continue inside ARC coordination workflows.");
            Add(guides, "Filter", "Isolate Selection", "View control", "Select the elements to inspect.", "Temporarily isolates selected elements in the active view.", "Reset temporary view before printing/exporting.");
            Add(guides, "Filter", "Hide Selection", "View control", "Select the elements to temporarily hide.", "Temporarily hides selected elements in the active view.", "This is temporary view state, not permanent deletion.");
            Add(guides, "Filter", "Reset Temporary View", "View control", "No selection is required.", "Clears active view temporary hide/isolate.", "Confirm all expected model categories are visible afterward.");
            Add(guides, "Filter", "Apply Color Review", "Visual QA", "Open a coordination view with ARC and coordination categories visible.", "Applies category review colors to visible elements.", "Clear overrides later if sheets require office graphics.");
            Add(guides, "Filter", "Save / Load Filter Preset", "Selection preset", "Select elements to save a preset, or run with no selection to load the last preset.", "Saves or restores a repeatable selection set.", "Preset validity depends on element IDs still existing in the model.");

            Add(guides, "Creation", "CAD to Model Manager", "CAD creation hub", "Select CAD imports/curves or open a view with CAD imports.", "Scans CAD layers, maps them to runnable actions, and lets you run checked rows.", "Review every action row before creating model elements.");
            Add(guides, "Creation", "Create Walls by Room", "Room creation", "Use selected bounded rooms or all bounded model rooms.", "Creates walls from reviewed room boundary segments.", "Check wall type, height/top constraint, joins, room bounding, and skipped boundaries.");
            Add(guides, "Creation", "Create Floors by Room", "Room creation", "Use selected bounded rooms or review rooms collected from the model.", "Creates one floor per selected room boundary.", "Check floor type, level, offset, duplicate floors, and room loops.");
            Add(guides, "Creation", "Create Ceilings by Room", "Room creation", "Use selected bounded rooms or review rooms collected from the model.", "Creates one ceiling per selected room boundary.", "Check ceiling type, level, height offset, and room loops.");
            Add(guides, "Creation", "Create Multiple Ceilings", "Room creation", "Review all bounded rooms across the chosen level scope.", "Creates multiple ceilings grouped by room and level.", "Check every ready row before creating ceilings in many rooms.");
            Add(guides, "Creation", "CAD to Walls", "CAD creation", "Select model/detail/CAD linework representing wall centerlines.", "Creates basic walls from projected curves.", "Check wall level, type, height, line projection, and duplicated walls.");
            Add(guides, "Creation", "CAD to Floors", "CAD creation", "Select closed CAD/model/detail floor boundary curves.", "Creates floors from closed loops.", "Check loop closure, floor type, level, and offset.");
            Add(guides, "Creation", "CAD to Ceilings", "CAD creation", "Select closed CAD/model/detail ceiling boundary curves.", "Creates ceilings from closed loops.", "Check loop closure, ceiling type, level, and height offset.");
            Add(guides, "Creation", "CAD to Rooms", "CAD creation", "Select closed boundaries inside a plan view.", "Places rooms at loop centroids.", "Check room placement, name/number, phase, and bounded area.");
            Add(guides, "Creation", "CAD to Room Boundaries", "Room setup", "Select line/arc curves in a plan view.", "Creates Revit room separation lines.", "Check that rooms become bounded and that separation lines are on the correct level.");
            Add(guides, "Creation", "CAD to Openings", "Opening setup", "Select closed opening boundaries.", "Creates MHNK opening candidate DirectShape solids.", "These are candidates; verify hosts and cut workflow separately.");
            Add(guides, "Creation", "CAD to Doors / Windows", "Door/window placement", "Select marker curves and load door/window family types.", "Places door/window candidates at curve midpoints.", "Use hosted placement commands when wall hosting is required.");
            Add(guides, "Creation", "Doors / Windows by Wall Side", "Door/window placement", "Select marker curves and optional host walls.", "Places hosted doors/windows on nearest walls and orients them toward marker side.", "Check swing/facing, host wall, level, family type, and sill height.");
            Add(guides, "Creation", "Doors / Windows by Position", "Door/window placement", "Select position markers and optional host walls.", "Places hosted doors/windows at projected positions on nearest walls.", "Check each instance against architectural opening intent.");
            Add(guides, "Creation", "Create ARC 3D View", "View setup", "Open any model view.", "Creates and opens a fine-detail ARC coordination 3D view.", "Rename or template the view if your office standard requires it.");
            Add(guides, "Creation", "Create Drafting View", "View setup", "Open the project.", "Creates an ARC drafting view.", "Use for detail coordination only; it does not create model geometry.");
            Add(guides, "Creation", "Create Working Plans", "View setup", "Make sure project levels are correct.", "Creates ARC working plan views for project levels.", "Check duplicate view names and view templates.");
            Add(guides, "Creation", "Create External Wall Finishes", "Finish modeling", "Select exterior host walls or open a view with visible exterior/basic walls.", "Creates exterior finish wall layers from host wall paths.", "Check side, thickness, material, offsets, room bounding, and joins.");
            Add(guides, "Creation", "Create Internal Wall Finishes", "Finish modeling", "Select interior host walls or open a view with visible host walls.", "Creates interior finish wall layers from host wall paths.", "Check side, thickness, material, room bounding, and duplicate finishes.");
            Add(guides, "Creation", "Create Floor Finishes", "Finish review", "Open a model with rooms and floors.", "Generates a floor-finish candidate report.", "Use the report to decide the next production floor-finish modeling step.");
            Add(guides, "Creation", "Create Ceiling Finishes", "Finish review", "Open a model with rooms and ceilings.", "Generates a ceiling-finish candidate report.", "Use the report to decide the next production ceiling-finish modeling step.");
            Add(guides, "Creation", "Create Model Group from Selection", "Model grouping", "Select elements that should become one model group.", "Creates a Revit model group from the selection.", "Avoid grouping elements that need independent editing or constraints.");

            Add(guides, "Edition", "Pin Selection", "Protection", "Select model elements to protect.", "Pins selected elements.", "Pinned elements still need workset/permission discipline.");
            Add(guides, "Edition", "Unpin Selection", "Protection", "Select pinned elements.", "Unpins selected elements.", "Only unpin elements you intend to edit.");
            Add(guides, "Edition", "Toggle Join / Unjoin", "Geometry edit", "Select exactly two model elements.", "Joins the pair if unjoined, or unjoins it if already joined.", "Check join cleanup in plan/section.");
            Add(guides, "Edition", "Join Selected Pairs", "Geometry edit", "Select intersecting model elements.", "Joins every allowed intersecting selected pair.", "Review skipped pairs and geometry cleanup.");
            Add(guides, "Edition", "Unjoin Selected Pairs", "Geometry edit", "Select elements with joined relationships.", "Unjoins joined pairs found inside the selection.", "Check wall/floor cleanup after unjoining.");
            Add(guides, "Edition", "Cut / Uncut", "Solid edit", "Select two solid-capable elements.", "Adds or removes a solid cut between the first two elements.", "Confirm which element cuts which before accepting final model state.");
            Add(guides, "Edition", "Cut / Uncut Selected Pairs", "Solid edit", "Select solid-capable intersecting elements.", "Toggles cuts for allowed selected pairs.", "Use on small selections first to avoid broad accidental cuts.");
            Add(guides, "Edition", "Cut Selected by First", "Solid edit", "Select cutter first, then targets.", "Uses the first selected element to cut other selected targets.", "Check target order and undo if cutter direction is wrong.");
            Add(guides, "Edition", "Uncut Selected Pairs", "Solid edit", "Select cut elements.", "Removes solid cuts found between selected pairs.", "Check openings or voids that were intentionally cut.");
            Add(guides, "Edition", "Switch Join Order", "Geometry edit", "Select two joined elements.", "Switches join order for the first two selected elements.", "Inspect detail cleanup after switching order.");
            Add(guides, "Edition", "Switch Join Order Selected", "Geometry edit", "Select joined model elements.", "Switches join order for joined pairs found inside the selection.", "Use on controlled selections only.");
            Add(guides, "Edition", "Set Room Bounding On", "Room setup", "Select walls or room-bounding-capable elements.", "Turns Room Bounding on where supported.", "Recalculate room areas/volumes after changing bounding behavior.");
            Add(guides, "Edition", "Set Room Bounding Off", "Room setup", "Select elements that should not bound rooms.", "Turns Room Bounding off where supported.", "Check rooms do not leak after changing this.");
            Add(guides, "Edition", "Batch Type Change", "Batch edit", "Select seed element first, then target elements.", "Changes target elements to the seed type.", "Confirm categories are compatible before running.");
            Add(guides, "Edition", "Split Walls Horizontally", "Stage 7 workflow", "Select walls or open a view with visible split-capable walls.", "Splits walls into lower and upper vertical segments at the configured height.", "Check lower/upper type assignment, height, top constraint, room bounding, and joins.");
            Add(guides, "Edition", "Lower Walls to Ceilings", "Stage 10 workflow", "Select walls and ceilings or open a view with visible walls/ceilings.", "Lowers wall tops to matching overlapping ceiling height plus offset.", "Check walls were lowered only where intended and not raised.");
            Add(guides, "Edition", "Batch Level Change", "Batch edit", "Select elements and provide a seed/active level.", "Moves supported selected elements to the resolved level.", "Check offsets and hosted relationships after level change.");
            Add(guides, "Edition", "Batch Offset Change", "Batch edit", "Select seed element first, then targets.", "Copies the seed offset value to target elements.", "Check base/top offsets and hosted elements after applying.");
            Add(guides, "Edition", "Batch Parameter Edit", "Batch edit", "Select seed element first, then target elements.", "Copies Mark/Comments style values from seed to targets.", "Avoid overwriting unique element marks unintentionally.");
            Add(guides, "Edition", "Align Elements", "Layout edit", "Select reference element first, then targets.", "Aligns target element centers to the first selected element center.", "Check whether center alignment matches your intended face/edge alignment.");
            Add(guides, "Edition", "Copy / Mirror / Array Presets", "Layout edit", "Select elements to copy.", "Creates a one-step offset copy using ARC settings.", "Review copied element hosting and constraints.");
            Add(guides, "Edition", "Clean Duplicate Elements", "QA selection", "Open a view or select suspected duplicates.", "Selects duplicate candidates without deleting them.", "Manually inspect before deleting anything.");
            Add(guides, "Edition", "Rename Views / Sheets", "Documentation edit", "Select views or sheets in the Project Browser.", "Prefixes selected views/sheets with MHNK where supported.", "Confirm office naming standard before running.");
            Add(guides, "Edition", "Reset Graphic Overrides", "Graphics cleanup", "Select ARC elements or open a view with visible ARC elements.", "Clears active view overrides for selected/visible ARC elements.", "Do not run on views where manual graphics are intentionally used.");

            Add(guides, "Solids", "Solids Interaction Center", "Geometry coordination", "Open a 3D coordination view or preselect elements.", "Retrieves controlled element lists, checks intersections/join/cut relationships, then performs enabled join, unjoin, switch, cut, or uncut operations with saved sets and CSV output.", "Run the matching check before editing; linked-model results are review/report only.");
            Add(guides, "Solids", "Create Bounding Solids", "Solid setup", "Select host elements.", "Creates DirectShape bounding boxes around selected elements.", "Use generated solids for review/candidate workflows, not final deliverables unless approved.");
            Add(guides, "Solids", "Select MHNK Solids", "Solid selection", "Open a view with MHNK-generated solids.", "Selects generated MHNK solids in the active view.", "Use before reports, cleanup, or visual review.");
            Add(guides, "Solids", "Solid Volume Report", "Quantity review", "Select solid-capable elements.", "Reports selected solid volume.", "Check units and element scope before using quantities.");
            Add(guides, "Solids", "Solid Area Report", "Quantity review", "Select solid-capable elements.", "Reports selected solid surface area.", "Check units and element scope before using quantities.");
            Add(guides, "Solids", "Intersection Check", "Coordination", "Select elements or open a view with coordination elements.", "Finds intersecting selected/visible elements.", "Review candidate pairs manually; intersection is not always a clash.");
            Add(guides, "Solids", "Clash Candidate Check", "Coordination", "Open an ARC/MEP coordination view.", "Finds probable ARC/MEP clash pairs.", "Prioritize real construction conflicts over harmless overlaps.");
            Add(guides, "Solids", "Opening Candidate Check", "Coordination", "Open a view with hosts and MEP penetrations.", "Finds wall/floor opening candidates from MEP intersections.", "Convert candidates into approved openings only after coordination review.");
            Add(guides, "Solids", "Cut Host by Solid", "Solid edit", "Select host and cutting solid.", "Toggles solid cut where Revit allows it.", "Confirm host/cutter order before accepting.");
            Add(guides, "Solids", "Cut / Uncut Selected Pairs", "Solid edit", "Select intersecting solid-capable elements.", "Toggles solid cuts for selected pairs.", "Use on controlled selections to avoid broad changes.");
            Add(guides, "Solids", "Cut Selected by First", "Solid edit", "Select cutter first, then targets.", "Cuts all allowed selected targets by the first element.", "Check every target after running.");
            Add(guides, "Solids", "Uncut Selected Pairs", "Solid edit", "Select cut solid-capable elements.", "Removes cuts found between selected pairs.", "Confirm intended openings are not removed.");
            Add(guides, "Solids", "Convert Solid to DirectShape", "Solid conversion", "Select solid geometry to copy.", "Creates MHNK DirectShape copies from selected solids.", "Keep generated copies organized and delete when no longer needed.");
            Add(guides, "Solids", "Color Solid Review", "Visual QA", "Open a view with generated or coordination solids.", "Applies review colors.", "Reset overrides before documentation if needed.");
            Add(guides, "Solids", "Delete MHNK Generated Solids", "Cleanup", "No selection required.", "Deletes MHNK-generated DirectShape solids from the model.", "Only generated MHNK solids should be removed; verify before running in production.");
            Add(guides, "Solids", "Export Solid Report", "Report export", "Select solids or open the relevant coordination view.", "Exports solid quantity/review results.", "Save the report with the model issue or coordination package.");

            Add(guides, "Xpress", "Model Health Report", "Fast audit", "Open the project or coordination view.", "Shows warnings, CAD imports, views, sheets, and ARC counts.", "Use as first check before bulk modeling.");
            Add(guides, "Xpress", "QA Dashboard", "Fast audit", "Open the project.", "Shows model health, CAD imports, warnings, required types, and mapping rules.", "Fix high-risk warnings before creation workflows.");
            Add(guides, "Xpress", "Workflow Validation Center", "Parity validation", "Open the Revit test model and active view used for checking tool behavior.", "Records pass, fail, blocked, and pending results per ARC tool and exports an HTML parity report.", "Mark Passed only after running the tool and comparing the model result with the expected workflow.");
            Add(guides, "Xpress", "CAD to Model Manager", "CAD creation hub", "Select CAD imports/curves or open a view with CAD imports.", "Scans CAD and runs mapped creation tools.", "Same caution as Creation CAD to Model Manager.");
            Add(guides, "Xpress", "Tool Settings", "Configuration", "No selection required.", "Opens ARC settings for CAD layer keywords, type hints, offsets, and clash tolerance.", "Changing settings affects future CAD-to-model workflows.");
            Add(guides, "Xpress", "Mapping Manager", "Configuration", "No selection required.", "Edits CAD layer to Revit action/type rules.", "Keep mapping rules simple and test on one CAD file first.");
            Add(guides, "Xpress", "Select CAD Imports", "CAD cleanup", "Open a view with CAD imports.", "Selects visible CAD imports in the active view.", "Use before cleaning, hiding, or scanning imports.");
            Add(guides, "Xpress", "Clean CAD Imports", "CAD cleanup", "Select CAD imports or open a view with visible CAD imports.", "Selects visible imports or temporarily hides selected imports.", "Do not delete source CAD until model creation is verified.");
            Add(guides, "Xpress", "Prepare ARC Workspace", "Workspace setup", "Open the project.", "Creates a coordination view and shows model health.", "Run this before major ARC/MEP coordination work.");
            Add(guides, "Xpress", "Create Coordination View", "Workspace setup", "Open the project.", "Creates and opens an ARC coordination 3D view.", "Apply project view template afterward if required.");
            Add(guides, "Xpress", "Check Warnings", "Fast audit", "Open the project.", "Shows the current Revit warning report.", "Resolve warnings affecting rooms, walls, joins, and hosts first.");
            Add(guides, "Xpress", "Check Missing Types", "Fast audit", "Open the project.", "Checks required ARC family/system types.", "Load or create missing types before running creation tools.");
            Add(guides, "Xpress", "Check Unjoined Walls", "Fast audit", "Open a view with walls.", "Finds wall join warnings and walls without joined neighbors.", "Review intentionally isolated walls before joining.");
            Add(guides, "Xpress", "Check Room Boundary Issues", "Fast audit", "Open a plan/model with rooms.", "Finds likely room boundary problems.", "Fix room separation, room bounding, and zero-area rooms before room-based creation.");
            Add(guides, "Xpress", "Check Openings", "Fast audit", "Open a view with openings/candidates.", "Finds opening coordination issues.", "Review against MEP and structural constraints.");
            Add(guides, "Xpress", "Auto Join ARC Elements", "Automation", "Select elements or open a view with visible ARC candidates.", "Automatically joins selected or visible ARC element pairs.", "Use controlled scopes; inspect joins afterward.");
            Add(guides, "Xpress", "Auto Color Review", "Visual QA", "Open a coordination view.", "Applies ARC review colors to visible categories.", "Reset overrides when returning to documentation graphics.");
            Add(guides, "Xpress", "Toggle Dark Theme", "UI setting", "No selection required.", "Switches MHNK ARC plugin windows between light and dark theme.", "Reopen the ARC menu to see the updated theme.");
            Add(guides, "Xpress", "Diagnostics Report", "Support", "No selection required.", "Shows deployment and support diagnostics.", "Use this when reporting install/runtime issues.");
            Add(guides, "Xpress", "Export QA Report", "Report export", "Open the model to audit.", "Exports model QA results.", "Attach the report to review packages or issue tracking.");

            return guides;
        }

        private static void Add(Dictionary<string, ToolGuide> guides, string category, string title, string useWhen, string before, string expected, string qa, string caution = null)
        {
            guides[GetKey(category, title)] = new ToolGuide(
                useWhen,
                Lines(before, "Confirm the active view and current selection match the intended scope.", "Save the model before bulk creation, geometry edits, or deletion."),
                Lines("Open MHNK ARC Tools and choose " + category + " > " + title + ".", "Review the details panel and any option/preview window.", "Run only when the ready rows and target scope are correct."),
                Lines(expected, "The command reports created, changed, selected, skipped, or found counts."),
                Lines(qa, "Inspect the result in plan/3D and use Undo immediately if the scope is wrong."),
                Lines(caution ?? "Run first on a controlled selection or test view when applying to many elements."));
        }

        private static IList<string> Lines(params string[] values)
        {
            return values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        private static string GetKey(MhnkArcCommandOption option)
        {
            return option == null ? "" : GetKey(option.Category, option.Title);
        }

        private static string GetKey(string category, string title)
        {
            return (category ?? "") + "|" + (title ?? "");
        }

        private static int GetCategoryOrder(string category)
        {
            for (int i = 0; i < CategoryOrder.Length; i++)
            {
                if (string.Equals(CategoryOrder[i], category, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return CategoryOrder.Length;
        }

        private static string GetAnchor(MhnkArcCommandOption option)
        {
            return Slug(option == null ? "all-tools" : option.Category + "-" + option.Title);
        }

        private static string Slug(string value)
        {
            var sb = new StringBuilder();
            bool lastDash = false;
            foreach (char c in (value ?? "").ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    lastDash = false;
                    continue;
                }

                if (!lastDash)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }

            return sb.ToString().Trim('-');
        }

        private static string Encode(string value)
        {
            return WebUtility.HtmlEncode(value ?? "");
        }

        private sealed class ToolGuide
        {
            public ToolGuide(string useWhen, IList<string> before, IList<string> process, IList<string> expected, IList<string> qa, IList<string> caution)
            {
                UseWhen = useWhen ?? "";
                Before = before ?? new List<string>();
                Process = process ?? new List<string>();
                Expected = expected ?? new List<string>();
                Qa = qa ?? new List<string>();
                Caution = caution ?? new List<string>();
            }

            public string UseWhen { get; private set; }
            public IList<string> Before { get; private set; }
            public IList<string> Process { get; private set; }
            public IList<string> Expected { get; private set; }
            public IList<string> Qa { get; private set; }
            public IList<string> Caution { get; private set; }
        }
    }
}
