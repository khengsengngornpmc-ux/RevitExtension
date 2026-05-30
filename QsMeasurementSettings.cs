using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json.Serialization;
#endif

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class QsMeasurementSettingsProfile
    {
        public string ProfileName { get; set; } = "MHNK Measurement Settings";
        public string Version { get; set; } = "1.2";
        public string UpdatedAtLocal { get; set; } = "";
        public List<QsMeasurementSettingRow> Rules { get; set; } = new List<QsMeasurementSettingRow>();

        public static QsMeasurementSettingsProfile CreateDefault()
        {
            var profile = new QsMeasurementSettingsProfile
            {
                ProfileName = "MHNK Measurement Settings",
                Version = "1.2",
                UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };

            int n = 10;
            AddExcavation(profile.Rules, ref n);
            AddFoundation(profile.Rules, ref n);
            AddColumn(profile.Rules, ref n);
            AddBeam(profile.Rules, ref n);
            AddWall(profile.Rules, ref n);
            AddSlab(profile.Rules, ref n);
            AddKerbAndOthers(profile.Rules, ref n);
            AddFinishRules(profile.Rules, ref n);
            AddExtendedStructureRules(profile.Rules, ref n);
            return profile;
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(ProfileName))
            {
                ProfileName = "MHNK Measurement Settings";
            }

            bool migrateTasColumnPriority = IsBeforeSettingsVersion(Version, 1, 1);
            bool migrateStairPaintingMethod = IsBeforeSettingsVersion(Version, 1, 2);

            if (string.IsNullOrWhiteSpace(Version))
            {
                Version = "1.0";
            }

            if (Rules == null)
            {
                Rules = new List<QsMeasurementSettingRow>();
            }

            MergeMissingDefaultRules();
            if (migrateTasColumnPriority)
            {
                ApplyTasColumnPriorityMigration();
            }

            if (migrateStairPaintingMethod)
            {
                ApplyStairPaintingMethodMigration();
            }

            int order = 10;
            foreach (QsMeasurementSettingRow row in Rules)
            {
                if (row == null) continue;
                ApplyMeasurementSettingChoices(row);
                if (row.SortOrder <= 0)
                {
                    row.SortOrder = order;
                }

                order = Math.Max(order + 10, row.SortOrder + 10);
            }

            if (migrateTasColumnPriority || migrateStairPaintingMethod)
            {
                Version = "1.2";
            }
        }

        public QsMeasurementSettingsProfile Clone()
        {
            QsMeasurementSettingsProfile clone = CamboBimJson.Deserialize<QsMeasurementSettingsProfile>(CamboBimJson.Serialize(this));
            if (clone == null)
            {
                clone = CreateDefault();
            }

            clone.Normalize();
            return clone;
        }

        public QsMeasurementSettingRow FindRule(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || Rules == null)
            {
                return null;
            }

            foreach (QsMeasurementSettingRow row in Rules)
            {
                if (row == null) continue;
                if (string.Equals(row.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            return null;
        }

        public bool GetBoolean(string code, bool fallback)
        {
            QsMeasurementSettingRow row = FindRule(code);
            if (row == null)
            {
                return fallback;
            }

            bool value;
            if (!TryParseBooleanValue(row.Value, out value))
            {
                value = fallback;
            }

            return row.IsEnabled && value;
        }

        public bool IsRuleEnabled(string code, bool fallback)
        {
            QsMeasurementSettingRow row = FindRule(code);
            return row == null ? fallback : row.IsEnabled;
        }

        public double GetDouble(string code, double fallback)
        {
            QsMeasurementSettingRow row = FindRule(code);
            if (row == null || !row.IsEnabled)
            {
                return fallback;
            }

            double value;
            return TryParseDoubleValue(row.Value, out value) ? value : fallback;
        }

        public string GetText(string code, string fallback)
        {
            QsMeasurementSettingRow row = FindRule(code);
            if (row == null || !row.IsEnabled)
            {
                return fallback;
            }

            string value = (row.Value ?? "").Trim();
            return value.Length == 0 ? fallback : value;
        }

        public bool GetChoiceEnabled(string code, bool fallback, params string[] disabledValues)
        {
            QsMeasurementSettingRow row = FindRule(code);
            if (row == null)
            {
                return fallback;
            }

            if (!row.IsEnabled)
            {
                return false;
            }

            bool boolValue;
            if (TryParseBooleanValue(row.Value, out boolValue))
            {
                return boolValue;
            }

            string text = (row.Value ?? "").Trim();
            if (text.Length == 0)
            {
                return fallback;
            }

            foreach (string disabledValue in disabledValues ?? Array.Empty<string>())
            {
                if (string.Equals(text, disabledValue, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        public bool GetChoiceContains(string code, string requiredText, bool fallback)
        {
            QsMeasurementSettingRow row = FindRule(code);
            if (row == null)
            {
                return fallback;
            }

            if (!row.IsEnabled)
            {
                return false;
            }

            bool boolValue;
            if (TryParseBooleanValue(row.Value, out boolValue))
            {
                return boolValue;
            }

            string text = (row.Value ?? "").Trim();
            if (text.Length == 0)
            {
                return fallback;
            }

            return string.IsNullOrWhiteSpace(requiredText) ||
                   text.IndexOf(requiredText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void MergeMissingDefaultRules()
        {
            QsMeasurementSettingsProfile defaults = CreateDefault();
            HashSet<string> existingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (QsMeasurementSettingRow row in Rules)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Code)) continue;
                existingCodes.Add(row.Code);
            }

            foreach (QsMeasurementSettingRow row in defaults.Rules)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Code)) continue;
                if (existingCodes.Contains(row.Code)) continue;

                Rules.Add(CopyRow(row));
                existingCodes.Add(row.Code);
            }
        }

        private void ApplyTasColumnPriorityMigration()
        {
            QsMeasurementSettingRow row = FindRule("COL.DEDUCT.BEAM");
            if (row == null) return;

            row.Description = "Column priority over beam";
            row.Option = "TAS column priority keeps column formwork gross; deduct the beam side instead";
            row.Value = "No";
            row.Method = "Priority";
            row.IsEnabled = false;
        }

        private void ApplyStairPaintingMethodMigration()
        {
            QsMeasurementSettingRow painting = FindRule("STAIR.PAINTING");
            if (painting != null)
            {
                painting.Value = "Yes";
                painting.IsEnabled = true;
            }

            QsMeasurementSettingRow bottom = FindRule("STAIR.BOTTOM");
            if (bottom != null)
            {
                bottom.Value = "No";
                bottom.IsEnabled = false;
                bottom.Option = "Disabled by TAS stair painting method";
                bottom.Method = "Legacy";
            }

            QsMeasurementSettingRow top = FindRule("STAIR.TOP");
            if (top != null)
            {
                top.Value = "No";
                top.IsEnabled = false;
                top.Option = "Disabled by TAS stair painting method";
                top.Method = "Legacy";
            }
        }

        private static bool IsBeforeSettingsVersion(string version, int major, int minor)
        {
            if (string.IsNullOrWhiteSpace(version)) return true;

            System.Version parsed;
            if (!System.Version.TryParse(version.Trim(), out parsed))
            {
                return true;
            }

            if (parsed.Major != major) return parsed.Major < major;
            return parsed.Minor < minor;
        }

        private static QsMeasurementSettingRow CopyRow(QsMeasurementSettingRow source)
        {
            return new QsMeasurementSettingRow
            {
                IsEnabled = source.IsEnabled,
                Category = source.Category,
                Code = source.Code,
                Description = source.Description,
                Option = source.Option,
                Value = source.Value,
                Unit = source.Unit,
                Method = source.Method,
                ValueChoices = CopyChoices(source.ValueChoices),
                MethodChoices = CopyChoices(source.MethodChoices),
                SortOrder = source.SortOrder
            };
        }

        private static List<string> CopyChoices(IEnumerable<string> source)
        {
            var choices = new List<string>();
            if (source == null)
            {
                return choices;
            }

            foreach (string item in source)
            {
                AddChoice(choices, item);
            }

            return choices;
        }

        private static bool TryParseBooleanValue(string value, out bool result)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0)
            {
                result = false;
                return false;
            }

            if (text.StartsWith("1 ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "True", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "On", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Calculate", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Calculate ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Include", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }

            if (text.StartsWith("0 ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "No", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "N", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "False", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Do not calculate", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Do not calculate ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Not calculate ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "Exclude", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static bool TryParseDoubleValue(string value, out double result)
        {
            string text = (value ?? "").Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                return true;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out result))
            {
                return true;
            }

            int greaterThanIndex = text.LastIndexOf('>');
            if (greaterThanIndex >= 0 && greaterThanIndex + 1 < text.Length)
            {
                string conditionValue = text.Substring(greaterThanIndex + 1).Trim();
                if (double.TryParse(conditionValue, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                {
                    return true;
                }

                if (double.TryParse(conditionValue, NumberStyles.Float, CultureInfo.CurrentCulture, out result))
                {
                    return true;
                }
            }

            string token = "";
            bool started = false;
            bool hasDecimal = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsDigit(c))
                {
                    token += c;
                    started = true;
                }
                else if ((c == '-' || c == '+') && !started)
                {
                    token += c;
                    started = true;
                }
                else if ((c == '.' || c == ',') && started && !hasDecimal)
                {
                    token += '.';
                    hasDecimal = true;
                }
                else if (started)
                {
                    break;
                }
            }

            return token.Length > 0 &&
                   double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        internal static void RefreshChoices(QsMeasurementSettingRow row)
        {
            ApplyMeasurementSettingChoices(row);
        }

        internal static bool TryParseMeasurementBoolean(string value, out bool result)
        {
            return TryParseBooleanValue(value, out result);
        }

        internal static bool TryParseMeasurementDouble(string value, out double result)
        {
            return TryParseDoubleValue(value, out result);
        }

        private static void ApplyMeasurementSettingChoices(QsMeasurementSettingRow row)
        {
            if (row == null)
            {
                return;
            }

            row.ValueChoices = BuildValueChoices(row);
            row.MethodChoices = BuildMethodChoices(row);
        }

        private static List<string> BuildValueChoices(QsMeasurementSettingRow row)
        {
            var choices = new List<string>();
            string code = row.Code ?? "";
            string method = row.Method ?? "";
            string unit = row.Unit ?? "";
            bool boolValue;
            double numberValue;

            if (IsExcavationWorkingSpaceCode(code))
            {
                AddChoices(choices, "0 Not calculate working space", "1 Calculate working space");
            }
            else if (IsExcavationSlopeCode(code))
            {
                AddChoices(choices, "0 Not calculate slope", "1 Calculate slope");
            }
            else if (IsFoundationSideSettingsCode(code))
            {
                AddChoice(choices, "Side Formwork Measurement Settings");
            }
            else if (IsFoundationSideSettingMethodCode(code))
            {
                AddFoundationSideMethodChoices(choices);
            }
            else if (string.Equals(code, "COL.STRUT.BOTTOM.PLANE", StringComparison.OrdinalIgnoreCase))
            {
                AddColumnBottomPlaneChoices(choices);
            }
            else if (string.Equals(code, "COL.STRUT.BOTTOM.FLOOR", StringComparison.OrdinalIgnoreCase))
            {
                AddColumnBottomFloorPrincipleChoices(choices);
            }
            else if (string.Equals(code, "COL.STRUT.TOP.PLANE", StringComparison.OrdinalIgnoreCase))
            {
                AddColumnTopPlaneChoices(choices);
            }
            else if (string.Equals(code, "COL.STRUT.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddColumnStruttingMethodChoices(choices);
            }
            else if (string.Equals(code, "COL.STRUT.JUDGE.START", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0.000", "3.900", "4.100", "4.500", "4.600", "5.000", "6.000");
            }
            else if (string.Equals(code, "COL.STRUT.CALC.START", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0.000", "3.500", "3.600", "3.900", "4.100", "4.500");
            }
            else if (string.Equals(code, "COL.STRUT.MAX.STAGES", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1", "2", "3", "4", "5", "10", "15");
            }
            else if (string.Equals(code, "COL.STRUT.STAGE.HEIGHT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.000", "1.200", "1.500", "2.000", "3.000");
            }
            else if (string.Equals(code, "COL.CONNECTION.ALLOWANCE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0", "1.0", "2.0", "2.5", "3.0", "5.0");
            }
            else if (string.Equals(code, "COL.SHAPE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Revit solid", "Type width/depth", "Bounding box");
            }
            else if (string.Equals(code, "BEAM.STRUT.BOTTOM.PLANE", StringComparison.OrdinalIgnoreCase))
            {
                AddBeamBottomPlaneChoices(choices);
            }
            else if (string.Equals(code, "BEAM.STRUT.TOP.PLANE", StringComparison.OrdinalIgnoreCase))
            {
                AddBeamTopPlaneChoices(choices);
            }
            else if (string.Equals(code, "BEAM.STRUT.METHOD", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "BEAM.STRUT.TOPFORM.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddBeamStruttingMethodChoices(choices);
            }
            else if (string.Equals(code, "BEAM.ARCHED.TOPFORM.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0 Not calculate top formwork", "1 Calculate top formwork");
            }
            else if (string.Equals(code, "BEAM.STRUT.CALC.START", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "BEAM.STRUT.JUDGE.START", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.500", "3.900", "4.100", "4.500", "4.600", "5.000", "6.000");
            }
            else if (string.Equals(code, "BEAM.STRUT.MAX.STAGES", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1", "2", "3", "4", "5", "10", "15");
            }
            else if (string.Equals(code, "BEAM.STRUT.STAGE.HEIGHT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.000", "1.200", "1.500", "2.000", "3.000");
            }
            else if (string.Equals(code, "BEAM.CONNECTION.ALLOWANCE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0", "1.0", "2.0", "2.5", "3.0", "5.0");
            }
            else if (string.Equals(code, "BEAM.LENGTH", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Location curve", "Analytical curve", "Bounding box");
            }
            else if (string.Equals(code, "SLAB.STRUT.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                AddBeamBottomPlaneChoices(choices);
            }
            else if (string.Equals(code, "SLAB.STRUT.TOP", StringComparison.OrdinalIgnoreCase))
            {
                AddSlabTopPlaneChoices(choices);
            }
            else if (string.Equals(code, "SLAB.STRUT.SOFFIT", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "SLAB.STRUT.EDGE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "SLAB.STRUT.TOPFWK", StringComparison.OrdinalIgnoreCase))
            {
                AddSlabStruttingMethodChoices(choices);
            }
            else if (string.Equals(code, "SLAB.TOP.ARCHED", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0 Not calculate top formwork", "1 Calculate top formwork");
            }
            else if (string.Equals(code, "SLAB.EDGE.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddSlabEdgeMethodChoices(choices);
            }
            else if (string.Equals(code, "SLAB.SLOPE.SOFFIT", StringComparison.OrdinalIgnoreCase))
            {
                AddSlabSlopeSoffitMethodChoices(choices);
            }
            else if (string.Equals(code, "SLAB.STRUT.JUDGE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.500", "3.900", "4.100", "4.500", "4.600", "5.000", "6.000");
            }
            else if (string.Equals(code, "SLAB.STRUT.START", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.500", "3.500", "3.600", "3.900", "4.100", "4.500", "4.600");
            }
            else if (string.Equals(code, "SLAB.STRUT.MAXSTAGE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1", "2", "3", "4", "5", "10", "15");
            }
            else if (string.Equals(code, "SLAB.STRUT.STAGEHEIGHT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.000", "1.200", "1.500", "2.000", "3.000");
            }
            else if (string.Equals(code, "SLAB.EDGE.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Segmentation standard of edge and break formwork", "<=0.250; <=0.500; <=1.000; thereafter 0.500", "Custom");
            }
            else if (string.Equals(code, "SLAB.OPENING.SIDE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Area (m2) >5.000", "Area (m2) >1.000", "Area (m2) >3.000", "Area (m2) >10.000");
            }
            else if (string.Equals(code, "WALL.STRUT.BOTTOM.PLANE", StringComparison.OrdinalIgnoreCase))
            {
                AddWallBottomPlaneChoices(choices);
            }
            else if (string.Equals(code, "WALL.STRUT.BOTTOM.FLOOR", StringComparison.OrdinalIgnoreCase))
            {
                AddWallBottomFloorPrincipleChoices(choices);
            }
            else if (string.Equals(code, "WALL.STRUT.TOP.PLANE", StringComparison.OrdinalIgnoreCase))
            {
                AddWallTopPlaneChoices(choices);
            }
            else if (string.Equals(code, "WALL.STRUT.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddWallStruttingMethodChoices(choices);
            }
            else if (string.Equals(code, "WALL.EDGE.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddWallEdgeMethodChoices(choices);
            }
            else if (string.Equals(code, "WALL.OPENING.ADDING.SIDE.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0 Add area of all sides of opening", "1 Add area of all sides except the soffit of opening");
            }
            else if (string.Equals(code, "WALL.STRUT.CALC.START", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "WALL.STRUT.JUDGE.START", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.500", "3.900", "4.100", "4.500", "4.600", "5.000", "6.000");
            }
            else if (string.Equals(code, "WALL.STRUT.MAX.STAGES", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1", "2", "3", "4", "5", "10", "15");
            }
            else if (string.Equals(code, "WALL.STRUT.STAGE.HEIGHT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.000", "1.200", "1.500", "2.000", "3.000");
            }
            else if (string.Equals(code, "WALL.EDGE.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Segmentation standard of edge and break formwork", "<=0.250; <=0.500; <=1.000; thereafter 0.500", "Custom");
            }
            else if (string.Equals(code, "WALL.OPENING.EDGE.CONDITION", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Area (m2) >5.000", "Area (m2) >1.000", "Area (m2) >3.000", "Area (m2) >10.000");
            }
            else if (string.Equals(code, "WALL.ENDCAP", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Joined ends excluded", "Include end caps", "Revit joined geometry");
            }
            else if (string.Equals(code, "WF.INT.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                AddWallFinishInteriorBottomChoices(choices, false);
            }
            else if (string.Equals(code, "WF.CUSTOM.INT.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                AddWallFinishInteriorBottomChoices(choices, true);
            }
            else if (string.Equals(code, "WF.INT.TOP", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "WF.CUSTOM.INT.TOP", StringComparison.OrdinalIgnoreCase))
            {
                AddWallFinishInteriorTopChoices(choices);
            }
            else if (string.Equals(code, "WF.EXT.METHOD", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "WF.CUSTOM.EXT.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddWallFinishExteriorChoices(choices);
            }
            else if (string.Equals(code, "WF.CUSTOM.SHOW", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0 No", "1 Yes");
            }
            else if (string.Equals(code, "WF.CUSTOM.LAYERS", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1", "2", "3", "4", "5");
            }
            else if (string.Equals(code, "WF.SUSPENDED.SETVALUE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "WF.CUSTOM.SUSPENDED.SETVALUE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0", "50", "100", "150");
            }
            else if (string.Equals(code, "WF.TILE.WASTE.MODE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0 Aesthetics first", "1 Cost first");
            }
            else if (string.Equals(code, "WF.TILE.WASTE.CUT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0", "5", "10", "15", "20");
            }
            else if (string.Equals(code, "CF.STRUT.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                AddBeamBottomPlaneChoices(choices);
            }
            else if (string.Equals(code, "CF.STRUT.TOP", StringComparison.OrdinalIgnoreCase))
            {
                AddCeilingFinishTopPlaneChoices(choices);
            }
            else if (string.Equals(code, "CF.STRUT.JUDGE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "CF.STRUT.START", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "3.000", "3.500", "3.600", "3.900", "4.100", "4.500", "4.600", "5.000", "6.000");
            }
            else if (string.Equals(code, "CF.STRUT.MAXSTAGE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1", "2", "3", "4", "5", "10", "15");
            }
            else if (string.Equals(code, "CF.STRUT.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddCeilingFinishStruttingMethodChoices(choices);
            }
            else if (string.Equals(code, "CF.STRUT.STAGEHEIGHT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.000", "1.200", "1.500", "2.000", "3.000");
            }
            else if (string.Equals(code, "SC.VERTICAL.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddSuspendedCeilingVerticalMethodChoices(choices);
            }
            else if (string.Equals(code, "SC.VERTICAL.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Segmentation standard of suspended ceiling to vertical surface", "<=0.500; thereafter 0.500", "Custom");
            }
            else if (string.Equals(code, "SC.STRUT.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                AddBeamBottomPlaneChoices(choices);
            }
            else if (string.Equals(code, "SC.STRUT.TOP", StringComparison.OrdinalIgnoreCase))
            {
                AddSuspendedCeilingTopPlaneChoices(choices);
            }
            else if (string.Equals(code, "SC.STRUT.JUDGE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "SC.STRUT.START", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "3.000", "3.500", "3.600", "3.900", "4.100", "4.500", "4.600", "5.000", "6.000");
            }
            else if (string.Equals(code, "SC.STRUT.MAXSTAGE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1", "2", "3", "4", "5", "10", "15");
            }
            else if (string.Equals(code, "SC.STRUT.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddCeilingFinishStruttingMethodChoices(choices);
            }
            else if (string.Equals(code, "SC.STRUT.STAGEHEIGHT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.000", "1.200", "1.500", "2.000", "3.000");
            }
            else if (string.Equals(code, "SC.AREA.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddSuspendedCeilingAreaMethodChoices(choices);
            }
            else if (string.Equals(code, "SC.AREA.SOFFIT", StringComparison.OrdinalIgnoreCase))
            {
                AddSuspendedCeilingSoffitPrincipleChoices(choices);
            }
            else if (string.Equals(code, "SC.AREA.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Segmentation standard of suspended ceiling", "<=0.500; thereafter 0.500", "Custom");
            }
            else if (string.Equals(code, "FF.VERTICAL.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddSuspendedCeilingVerticalMethodChoices(choices);
            }
            else if (string.Equals(code, "FF.VERTICAL.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Segmentation standard of floor finish to vertical surface", "<=0.150; <=0.225; <=0.300; thereafter 0.075", "Custom");
            }
            else if (string.Equals(code, "FF.TILE.WASTE.MODE", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0 Aesthetics first", "1 Cost first");
            }
            else if (string.Equals(code, "FF.TILE.WASTE.CUT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0", "5", "10", "15", "20");
            }
            else if (string.Equals(code, "LINTEL.CANTILEVER.MAX", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0", "50", "100", "150", "200", "250", "300", "500");
            }
            else if (string.Equals(code, "LINTEL.ARCHED.TOPFORM.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0 Not calculate top formwork", "1 Calculate top formwork");
            }
            else if (string.Equals(code, "DROP.STRUT.BOTTOM.PLANE", StringComparison.OrdinalIgnoreCase))
            {
                AddDropPanelBottomPlaneChoices(choices);
            }
            else if (string.Equals(code, "DROP.STRUT.BOTTOM.FLOOR", StringComparison.OrdinalIgnoreCase))
            {
                AddDropPanelBottomFloorPrincipleChoices(choices);
            }
            else if (string.Equals(code, "DROP.STRUT.TOP.PLANE", StringComparison.OrdinalIgnoreCase))
            {
                AddDropPanelTopPlaneChoices(choices);
            }
            else if (string.Equals(code, "DROP.STRUT.JUDGE.START", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "DROP.STRUT.CALC.START", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "3.500", "3.600", "3.900", "4.100", "4.500", "4.600", "5.000", "6.000");
            }
            else if (string.Equals(code, "DROP.STRUT.MAX.STAGES", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1", "2", "3", "4", "5", "10", "15");
            }
            else if (string.Equals(code, "DROP.STRUT.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddDropPanelStruttingMethodChoices(choices);
            }
            else if (string.Equals(code, "DROP.STRUT.STAGE.HEIGHT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "1.000", "1.200", "1.500", "2.000", "3.000");
            }
            else if (string.Equals(code, "STAIR.SIDE.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddStaircaseSideMethodChoices(choices);
            }
            else if (string.Equals(code, "STAIR.SIDE.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Segmentation standard of side formwork", "<=0.250; <=0.500; <=1.000; thereafter 0.500", "Custom");
            }
            else if (string.Equals(code, "ROOF.WP.VERTICAL.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                AddWaterproofVerticalMethodChoices(choices);
            }
            else if (string.Equals(code, "ROOF.WP.VERTICAL.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Segmentation standard of waterproof to vertical surface", "<=0.150; <=0.225; <=0.300; thereafter 0.075", "Custom");
            }
            else if (string.Equals(code, "KERB.LENGTH", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Model length", "Solid perimeter", "Bounding box");
            }
            else if (string.Equals(code, "OTHER.CLASSIFY", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Type mapping", "Category", "CBIM BOQ mapping");
            }
            else if (IsRoomGroupingCode(code))
            {
                AddChoices(choices, "Level + Room", "Level only", "Level + Type", "Level + Room + Type", "Type only", "BOQ code");
            }
            else if (IsSegmentationCode(code) || string.Equals(method, "Segmentation", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Profile", "<=0.250; <=0.500; <=1.000; thereafter 0.500", "Custom");
            }
            else if (string.Equals(method, "Elevation", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Option 1", "Option 2");
            }
            else if (string.Equals(method, "Staged", StringComparison.OrdinalIgnoreCase) ||
                     StartsWithOption(row.Value))
            {
                AddChoices(choices, "Option 0", "Option 1", "Option 2", "Option 3");
            }
            else if (string.Equals(method, "Numeric", StringComparison.OrdinalIgnoreCase) ||
                     (string.Equals(method, "Condition", StringComparison.OrdinalIgnoreCase) &&
                      TryParseDoubleValue(row.Value, out numberValue)))
            {
                AddNumericChoices(choices, unit);
            }
            else if (string.Equals(method, "Option", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(method, "Deduction", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(method, "Area", StringComparison.OrdinalIgnoreCase) ||
                     TryParseBooleanValue(row.Value, out boolValue))
            {
                AddChoices(choices, "Yes", "No");
            }
            else if (string.Equals(method, "Condition", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Yes", "No");
            }

            AddChoice(choices, row.Value);
            return choices;
        }

        private static List<string> BuildMethodChoices(QsMeasurementSettingRow row)
        {
            var choices = new List<string>();
            string current = row?.Method ?? "";
            string code = row?.Code ?? "";

            if (string.Equals(current, "Deduction", StringComparison.OrdinalIgnoreCase) ||
                code.IndexOf(".DEDUCT.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddChoices(choices, "Deduction", "Option", "Condition");
            }
            else if (string.Equals(current, "Condition", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Condition", "Numeric", "Option");
            }
            else if (string.Equals(current, "Numeric", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Numeric", "Condition");
            }
            else if (string.Equals(current, "Area", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Area", "Option", "Condition");
            }
            else if (string.Equals(current, "Method", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Method", "Option");
            }
            else if (string.Equals(current, "Elevation", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Elevation", "Staged", "Method");
            }
            else if (string.Equals(current, "Staged", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Staged", "Elevation", "Option", "Method");
            }
            else if (string.Equals(current, "Settings", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Settings", "Method", "Option");
            }
            else if (string.Equals(current, "Classification", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Classification", "Method");
            }
            else if (string.Equals(current, "Segmentation", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "Segmentation", "Numeric", "Method");
            }
            else if (string.Equals(current, "Option", StringComparison.OrdinalIgnoreCase) ||
                     string.IsNullOrWhiteSpace(current))
            {
                AddChoices(choices, "Option", "Condition", "Deduction");
            }
            else
            {
                AddChoices(
                    choices,
                    "Option",
                    "Deduction",
                    "Condition",
                    "Method",
                    "Numeric",
                    "Elevation",
                    "Staged",
                    "Settings",
                    "Area",
                    "Classification",
                    "Segmentation");
            }

            AddChoice(choices, current);
            return choices;
        }

        private static bool IsRoomGroupingCode(string code)
        {
            return code != null &&
                   code.EndsWith(".ROOM", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExcavationWorkingSpaceCode(string code)
        {
            return !string.IsNullOrWhiteSpace(code) &&
                   code.StartsWith("EXC.WORKSPACE.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExcavationSlopeCode(string code)
        {
            return !string.IsNullOrWhiteSpace(code) &&
                   code.StartsWith("EXC.SLOPE.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFoundationSideSettingsCode(string code)
        {
            return string.Equals(code, "FOUN.SIDE.SETTINGS", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFoundationSideSettingMethodCode(string code)
        {
            return !string.IsNullOrWhiteSpace(code) &&
                   code.StartsWith("FOUN.SIDE.SETTING.", StringComparison.OrdinalIgnoreCase) &&
                   code.EndsWith(".METHOD", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSegmentationCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            return code.EndsWith(".SEGMENT", StringComparison.OrdinalIgnoreCase) ||
                   code.EndsWith(".SEGMENTATION", StringComparison.OrdinalIgnoreCase) ||
                   code.IndexOf(".HEIGHT.SEGMENT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool StartsWithOption(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Trim().StartsWith("Option", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddNumericChoices(List<string> choices, string unit)
        {
            string normalizedUnit = (unit ?? "").Trim();
            if (string.Equals(normalizedUnit, "m2", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0.100", "0.300", "0.500", "1.000", "5.000");
            }
            else if (string.Equals(normalizedUnit, "m", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0.300", "0.500", "1.000", "1.500", "2.000", "3.000");
            }
            else if (string.Equals(normalizedUnit, "deg", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "5", "10", "15", "30", "45");
            }
            else if (string.Equals(normalizedUnit, "stage", StringComparison.OrdinalIgnoreCase))
            {
                AddChoices(choices, "0", "1", "2", "3", "5", "10");
            }
            else
            {
                AddChoices(choices, "0", "1", "2", "5", "10");
            }
        }

        private static void AddChoices(List<string> choices, params string[] values)
        {
            if (values == null)
            {
                return;
            }

            foreach (string value in values)
            {
                AddChoice(choices, value);
            }
        }

        private static void AddChoice(List<string> choices, string value)
        {
            if (choices == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string text = value.Trim();
            foreach (string existing in choices)
            {
                if (string.Equals(existing, text, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            choices.Add(text);
        }

        private static void AddFoundationSideMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate in stages: calculate by area",
                FoundationSideDefaultMethodValue(),
                "2 Calculate in stages: calculate by area");
        }

        internal static string FoundationSideDefaultMethodValue()
        {
            return "1 Calculate in stages: calculate by length if the height is less than the set value; otherwise, calculate by area";
        }

        private static void AddColumnBottomPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select floor",
                "1 Select ground elevation or floor",
                "2 Select floor if with basement; select ground elevation or floor if without basement",
                "3 Select soffit of entity");
        }

        private static void AddDropPanelBottomPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select floor",
                "1 Select ground elevation or floor",
                "2 Select floor if with basement; select ground elevation or floor if without basement");
        }

        private static void AddColumnBottomFloorPrincipleChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select the bottom elevation of current floor",
                "1 If the bottom elevation of column is intersecting with or tangent to floor slabs, select the floor slab with higher top elevation; if not, select the floor slab with shorter distance from the column bottom elevation",
                "2 If the bottom elevation of column is intersecting with or tangent to floor slabs, select the floor slab with higher top elevation; if not, select the floor slab with longer distance from the column bottom elevation",
                "3 If the bottom elevation of column is intersecting with or tangent to floor slabs, select the floor slab with lower top elevation; if not, select the floor slab with shorter distance from the column bottom elevation",
                "4 If the bottom elevation of column is intersecting with or tangent to floor slabs, select the floor slab with lower top elevation; if not, select the floor slab with longer distance from the column bottom elevation");
        }

        private static void AddDropPanelBottomFloorPrincipleChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select the bottom elevation of current floor",
                "1 If the bottom elevation of column below drop panel is intersecting with or tangent to floor slabs, select the floor slab with higher top elevation; if not, select the floor slab with shorter distance from the bottom elevation of column below drop panel",
                "2 If the bottom elevation of column below drop panel is intersecting with or tangent to floor slabs, select the floor slab with higher top elevation; if not, select the floor slab with longer distance from the bottom elevation of column below drop panel",
                "3 If the bottom elevation of column below drop panel is intersecting with or tangent to floor slabs, select the floor slab with lower top elevation; if not, select the floor slab with shorter distance from the bottom elevation of column below drop panel",
                "4 If the bottom elevation of column below drop panel is intersecting with or tangent to floor slabs, select the floor slab with lower top elevation; if not, select the floor slab with longer distance from the bottom elevation of column below drop panel");
        }

        private static void AddColumnTopPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select top elevation of column",
                "1 Select the value of column top elevation minus slab thickness",
                "2 Select the value of column top elevation minus beam height");
        }

        private static void AddDropPanelTopPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select top elevation of drop panel",
                "1 Select the value of drop panel top elevation minus slab thickness");
        }

        private static void AddColumnStruttingMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate strutting high in stages: calculate total quantities",
                "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)",
                "2 Not calculate strutting high: calculate formwork in stages",
                "3 Not calculate strutting high");
        }

        private static void AddDropPanelStruttingMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate strutting high in stages: calculate total quantities",
                "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)",
                "2 Not calculate strutting high");
        }

        private static void AddBeamBottomPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select slab top elevation if slab exists (in current floor and lower floor), select floor bottom elevation of the current floor if no slab exists",
                "1 Select slab top elevation if slab exists (in current floor and lower floor), select floor bottom elevation of the lower floor if no slab exists",
                "2 Select slab top elevation if slab exists (in current floor and all lower floors), select floor bottom elevation of the first floor if no slab exists");
        }

        private static void AddBeamTopPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select beam top elevation for flat beam, select maximum beam top elevation for sloping beam",
                "1 Select beam bottom elevation for flat beam, select maximum beam bottom elevation for sloping beam",
                "2 Select beam top elevation for flat beam, select average beam top elevation for sloping beam",
                "3 Select beam bottom elevation for flat beam, select average beam bottom elevation for sloping beam",
                "4 Select the bottom elevation of slab intersecting with beam for flat beam, select average bottom elevation of slab intersecting with beam for sloping beam",
                "5 Select the bottom elevation of slab intersecting with beam for flat beam, select maximum bottom elevation of slab intersecting with beam for sloping beam");
        }

        private static void AddBeamStruttingMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate strutting high in stages: calculate total quantities",
                "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)",
                "2 Not calculate strutting high");
        }

        private static void AddSlabTopPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select slab top elevation for flat slab, select maximum slab top elevation for sloping slab",
                "1 Select slab bottom elevation for flat slab, select maximum slab bottom elevation for sloping slab",
                "2 Select slab top elevation for flat slab, select average slab top elevation for sloping slab",
                "3 Select slab bottom elevation for flat slab, select average slab bottom elevation for sloping slab");
        }

        private static void AddSlabStruttingMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate strutting high in stages: calculate total quantities",
                "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)",
                "2 Not calculate strutting high");
        }

        private static void AddSlabEdgeMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate in stages: calculate by area",
                "1 Calculate in stages: calculate by length if the height is less than the set value; otherwise, calculate by area",
                "2 Calculate in stages: calculate by area");
        }

        private static void AddSlabSlopeSoffitMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate in stages: calculate by area, and incorporate its quantity into that of soffit formwork",
                "1 Calculate in stages: calculate by length if less than the set value; otherwise, calculate by area, and incorporate its quantity into that of soffit formwork",
                "2 Calculate in stages: calculate by area");
        }

        private static void AddWallBottomPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select floor for both flat wall and sloping wall",
                "1 Select ground elevation or floor for both flat wall and sloping wall",
                "2 For both flat wall and sloping wall, select floor if with basement; select ground elevation or floor if without basement",
                "3 Select floor for flat wall, select average wall bottom elevation for sloping wall",
                "4 Select ground elevation or floor for flat wall, select average wall bottom elevation for sloping wall",
                "5 For flat wall, select floor if with basement, select ground elevation or floor if without basement; for sloping wall, select average wall bottom elevation");
        }

        private static void AddWallBottomFloorPrincipleChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select the bottom elevation of current floor",
                "1 If the bottom elevation of wall is intersecting with or tangent to floor slabs, select the floor slab with higher top elevation; if not, select the floor slab with shorter distance from the wall bottom elevation",
                "2 If the bottom elevation of wall is intersecting with or tangent to floor slabs, select the floor slab with higher top elevation; if not, select the floor slab with longer distance from the wall bottom elevation",
                "3 If the bottom elevation of wall is intersecting with or tangent to floor slabs, select the floor slab with lower top elevation; if not, select the floor slab with shorter distance from the wall bottom elevation",
                "4 If the bottom elevation of wall is intersecting with or tangent to floor slabs, select the floor slab with lower top elevation; if not, select the floor slab with shorter distance from the wall bottom elevation");
        }

        private static void AddWallTopPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select floor top elevation",
                "1 Select wall top elevation for flat wall, select maximum wall top elevation for sloping wall",
                "2 Select wall top elevation for flat wall, select average wall top elevation for sloping wall",
                "3 Select slab bottom elevation for flat wall, select maximum slab bottom elevation for sloping wall",
                "4 Select slab bottom elevation for flat wall, select average slab bottom elevation for sloping wall",
                "5 Select minimum bottom elevation of beam below slab for flat wall, select minimum average bottom elevation of beam below slab for sloping wall");
        }

        private static void AddWallStruttingMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate strutting high in stages: calculate total quantities",
                "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)",
                "2 Calculate strutting high in stages: classify by strutting high height",
                "3 Not calculate strutting high");
        }

        private static void AddWallEdgeMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate in stages: calculate by area",
                "1 Calculate in stages: calculate by length if the width is less than the set value; otherwise, calculate by area",
                "2 Calculate in stages: calculate by area");
        }

        private static void AddWallFinishInteriorBottomChoices(List<string> choices, bool includeFloorBottomSetValue)
        {
            AddChoices(
                choices,
                "0 Calculate height from skirting",
                "1 Calculate height from floor");

            if (includeFloorBottomSetValue)
            {
                AddChoice(choices, "2 Select floor bottom elevation + set value");
            }
        }

        private static void AddWallFinishInteriorTopChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 If with suspended ceiling, select suspended ceiling bottom elevation + set value; otherwise, select slab bottom elevation",
                "1 Select slab bottom elevation",
                "2 Select floor bottom elevation + set value");
        }

        private static void AddWallFinishExteriorChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 If with basement, calculate from floor bottom elevation; if without basement, calculate from ground elevation for first floor, and calculate from floor bottom elevation for other floors",
                "1 Calculate from ground elevation");
        }

        private static void AddCeilingFinishTopPlaneChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Select slab bottom elevation for flat ceiling finish, select maximum slab bottom elevation for sloping ceiling finish",
                "1 Select slab bottom elevation for flat ceiling finish, select average slab bottom elevation for sloping ceiling finish");
        }

        private static void AddCeilingFinishStruttingMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate strutting high in stages: calculate total quantities",
                "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)",
                "2 Not calculate strutting high");
        }

        private static void AddSuspendedCeilingVerticalMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate in stages: calculate by area",
                "1 Calculate in stages: calculate by length if the height of vertical surface is less than the set value; otherwise, calculate by area",
                "2 Calculate in stages: calculate by area");
        }

        private static void AddStaircaseSideMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate in stages: calculate by area",
                "1 Calculate in stages: calculate by length if the height is less than the set value; otherwise, calculate by area",
                "2 Calculate in stages: calculate by area");
        }

        private static void AddWaterproofVerticalMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate in stages: calculate by area",
                "1 Calculate in stages: calculate by length if the height of vertical surface is less than the set value; otherwise, calculate by area",
                "2 Calculate in stages: calculate by area");
        }

        private static void AddSuspendedCeilingTopPlaneChoices(List<string> choices)
        {
            AddChoice(choices, "0 Select bottom elevation of suspended ceiling");
        }

        private static void AddSuspendedCeilingAreaMethodChoices(List<string> choices)
        {
            AddChoices(
                choices,
                "0 Not calculate in stages: calculate by area",
                "1 Calculate in stages: calculate by area");
        }

        private static void AddSuspendedCeilingSoffitPrincipleChoices(List<string> choices)
        {
            AddChoice(
                choices,
                "0 If there are slabs in the current floor, select the bottom elevation of top slab with shortest distance from the soffit of suspended ceiling; otherwise, select the top elevation of current floor");
        }

        private static void AddExcavation(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Excavation", "EXC.WORKSPACE.TRENCH", "Method for calculating working space of trench excavation", "Calculate or skip working space allowance for trench excavation", "1 Calculate working space", "-", "Option", true);
            Row(rows, ref order, "Excavation", "EXC.WORKSPACE.HEAVY", "Method for calculating working space of heavy excavation", "Calculate or skip working space allowance for heavy excavation", "1 Calculate working space", "-", "Option", true);
            Row(rows, ref order, "Excavation", "EXC.WORKSPACE.PIT", "Method for calculating working space of pit excavation", "Calculate or skip working space allowance for pit excavation", "1 Calculate working space", "-", "Option", true);
            Row(rows, ref order, "Excavation", "EXC.SLOPE.TRENCH", "Method for calculating slope of trench excavation", "Calculate or skip sloped excavation side allowance", "1 Calculate slope", "-", "Option", true);
            Row(rows, ref order, "Excavation", "EXC.SLOPE.HEAVY", "Method for calculating slope of heavy excavation", "Calculate or skip sloped excavation side allowance", "1 Calculate slope", "-", "Option", true);
            Row(rows, ref order, "Excavation", "EXC.SLOPE.PIT", "Method for calculating slope of pit excavation", "Calculate or skip sloped excavation side allowance", "1 Calculate slope", "-", "Option", true);
        }

        private static void AddFoundation(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Foundation", "FOUN.RAFT.SIDE.SLOPE", "Provide formwork to side of raft foundation if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers raft foundation side formwork", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Foundation", "FOUN.STRIP.SIDE.SLOPE", "Provide formwork to side of strip foundation if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers strip foundation side formwork", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Foundation", "FOUN.PILECAP.SIDE.SLOPE", "Provide formwork to side of pile cap if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers pile cap side formwork", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Foundation", "FOUN.GROUNDBEAM.SIDE.SLOPE", "Provide formwork to side of ground beam if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers ground beam side formwork", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Foundation", "FOUN.PAD.SIDE.SLOPE", "Provide formwork to side of pad foundation if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers pad foundation side formwork", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Foundation", "FOUN.POSTCAST.MAXEXT", "Maximum auto extension of post cast strip (mm)", "Maximum post-cast strip auto extension", "500", "mm", "Numeric", true);
            Row(rows, ref order, "Foundation", "FOUN.GROUNDBEAM.TOP.SLOPE", "Provide formwork to top of ground beam if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers ground beam top formwork", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Foundation", "FOUN.SIDE.SETTINGS", "Side Formwork Measurement Settings", "Open side formwork method and segmentation settings", "Side Formwork Measurement Settings", "-", "Settings", true);

            AddFoundationSideElementSettings(rows, ref order, "BLINDING", "Blinding");
            AddFoundationSideElementSettings(rows, ref order, "PAD", "Pad Foundation");
            AddFoundationSideElementSettings(rows, ref order, "PILECAP", "Pile Cap");
            AddFoundationSideElementSettings(rows, ref order, "RAFT", "Raft Foundation");
            AddFoundationSideElementSettings(rows, ref order, "GROUNDBEAM", "Ground Beam");
            AddFoundationSideElementSettings(rows, ref order, "STRIP", "Strip Foundation");

            Row(rows, ref order, "Foundation", "FOUN.SIDE", "Calculate side formwork", "Calculate exposed vertical and sloping sides", "Yes", "-", "Option", true);
            Row(rows, ref order, "Foundation", "FOUN.TOP", "Calculate upper inclined/top face", "Include upper inclined foundation face", "Yes", "-", "Option", true);
            Row(rows, ref order, "Foundation", "FOUN.DEDUCT.FOUN", "Deduct intersecting foundation", "Deduct overlapped formwork area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Foundation", "FOUN.DEDUCT.BEAM", "Deduct intersecting beam", "Deduct overlapped formwork area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Foundation", "FOUN.DEDUCT.COL", "Deduct intersecting column", "Deduct overlapped formwork area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Foundation", "FOUN.DEDUCT.WALL", "Deduct intersecting wall", "Deduct overlapped formwork area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Foundation", "FOUN.DEDUCT.FLOOR", "Deduct intersecting floor/slab", "Deduct overlapped formwork area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Foundation", "FOUN.DEDUCT.OTHER", "Deduct other concrete/generic intersections", "Deduct generic concrete contact area", "Yes", "-", "Deduction", true);
        }

        private static void AddFoundationSideElementSettings(List<QsMeasurementSettingRow> rows, ref int order, string key, string label)
        {
            string prefix = "FOUN.SIDE.SETTING." + key + ".";
            Row(rows, ref order, "Foundation", prefix + "METHOD", "Method for calculating side formwork", label, FoundationSideDefaultMethodValue(), "-", "Method", true);
            Row(rows, ref order, "Foundation", prefix + "CONDITION", "Condition for calculating side formwork in stages", label, "1.000", "m", "Numeric", true);
            Row(rows, ref order, "Foundation", prefix + "SEG.1", "Segmentation Standard", label, "0.250", "m", "Segmentation", true);
            Row(rows, ref order, "Foundation", prefix + "SEG.2", "Segmentation Standard", label, "0.500", "m", "Segmentation", true);
            Row(rows, ref order, "Foundation", prefix + "SEG.3", "Segmentation Standard", label, "1.000", "m", "Segmentation", true);
            Row(rows, ref order, "Foundation", prefix + "THEREAFTER", "And thereafter in m stages", label, "0.500", "m", "Numeric", true);
        }

        private static void AddColumn(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Column", "COL.STRUT.BOTTOM.PLANE", "Method for calculating bottom plane of strutting high", "Select the lower reference plane used for column high-support formwork", "3 Select soffit of entity", "-", "Method", true);
            Row(rows, ref order, "Column", "COL.STRUT.BOTTOM.FLOOR", "Principle of strutting high bottom plane selecting floor", "Select the floor slab reference when the bottom plane method uses floor selection", "4 If the bottom elevation of column is intersecting with or tangent to floor slabs, select the floor slab with lower top elevation; if not, select the floor slab with longer distance from the column bottom elevation", "-", "Method", true);
            Row(rows, ref order, "Column", "COL.STRUT.TOP.PLANE", "Method for calculating top plane of strutting high", "Select the upper reference plane used for column high-support formwork", "0 Select top elevation of column", "-", "Method", true);
            Row(rows, ref order, "Column", "COL.STRUT.JUDGE.START", "Starting height to judge strutting high (m)", "Height threshold used to determine whether high-support formwork applies", "0.000", "m", "Numeric", true);
            Row(rows, ref order, "Column", "COL.STRUT.CALC.START", "Starting height to calculate strutting high (m)", "Height threshold used as the starting point for high-support quantities", "0.000", "m", "Numeric", true);
            Row(rows, ref order, "Column", "COL.STRUT.MAX.STAGES", "Maximum number of stages for strutting high", "Maximum stage count for column high-support formwork", "10", "stage", "Numeric", true);
            Row(rows, ref order, "Column", "COL.STRUT.METHOD", "Method for calculating strutting high", "Choose whether high-support formwork is measured by total quantity, separate stages, staged formwork, or skipped", "2 Not calculate strutting high: calculate formwork in stages", "-", "Method", true);
            Row(rows, ref order, "Column", "COL.STRUT.STAGE.HEIGHT", "Height of stage for strutting high (m)", "Vertical height of each high-support stage", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Column", "COL.CORBEL.TOP.SLOPE", "Provide formwork to top of corbel if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers corbel top formwork", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Column", "COL.CONNECTION.ALLOWANCE", "Fixing percentage of allowance for connections (%)", "Percentage allowance added for column connection formwork", "2.5", "%", "Numeric", true);

            Row(rows, ref order, "Column", "COL.SIDE", "Calculate side formwork", "Calculate all exposed side faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Column", "COL.TOPBOTTOM", "Calculate top/bottom formwork", "Do not calculate top or bottom faces", "No", "-", "Option", false);
            Row(rows, ref order, "Column", "COL.DEDUCT.BEAM", "Column priority over beam", "TAS column priority keeps column formwork gross; deduct the beam side instead", "No", "-", "Priority", false);
            Row(rows, ref order, "Column", "COL.DEDUCT.BEAM.SIZE", "Beam size priority", "Deduct only when beam size is greater than or equal to column", "No", "-", "Condition", false);
            Row(rows, ref order, "Column", "COL.DEDUCT.FLOOR", "Deduct floor/slab intersection", "Deduct slab or floor contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Column", "COL.DEDUCT.WALL", "Deduct wall intersection", "Deduct wall contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Column", "COL.DEDUCT.FOUN", "Deduct foundation intersection", "Deduct foundation contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Column", "COL.DEDUCT.COL", "Deduct column-to-column intersection", "Deduct overlapping column contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Column", "COL.DEDUCT.OTHER", "Deduct other concrete/generic intersections", "Deduct generic concrete contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Column", "COL.SHAPE", "Section shape method", "Use Revit solid face area; fallback to type width/depth", "Revit solid", "-", "Method", true);
        }

        private static void AddBeam(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Beam", "BEAM.STRUT.BOTTOM.PLANE", "Method for calculating bottom plane of strutting high", "Select slab/floor reference used as the lower plane for beam high-support formwork", "1 Select slab top elevation if slab exists (in current floor and lower floor), select floor bottom elevation of the lower floor if no slab exists", "-", "Method", true);
            Row(rows, ref order, "Beam", "BEAM.STRUT.TOP.PLANE", "Method for calculating top plane of strutting high", "Select beam or intersecting slab reference used as the upper plane for beam high-support formwork", "1 Select beam bottom elevation for flat beam, select maximum beam bottom elevation for sloping beam", "-", "Method", true);
            Row(rows, ref order, "Beam", "BEAM.STRUT.CALC.START", "Starting height to calculate strutting high (m)", "Height threshold used as the starting point for beam high-support quantities", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Beam", "BEAM.STRUT.JUDGE.START", "Starting height to judge strutting high (m)", "Height threshold used to determine whether beam high-support formwork applies", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Beam", "BEAM.STRUT.MAX.STAGES", "Maximum number of stages for strutting high", "Maximum stage count for beam high-support formwork", "10", "stage", "Numeric", true);
            Row(rows, ref order, "Beam", "BEAM.STRUT.METHOD", "Method for calculating strutting high", "Choose whether beam high-support formwork is measured by total quantity, separate stages, or skipped", "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)", "-", "Method", true);
            Row(rows, ref order, "Beam", "BEAM.STRUT.TOPFORM.METHOD", "Method for calculating top formwork for strutting high", "Choose how beam top formwork is handled for high-support conditions", "2 Not calculate strutting high", "-", "Method", true);
            Row(rows, ref order, "Beam", "BEAM.STRUT.STAGE.HEIGHT", "Height of stage for strutting high (m)", "Vertical height of each beam high-support stage", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Beam", "BEAM.TOP.SLOPE", "Provide formwork to top of beam if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers beam top formwork", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Beam", "BEAM.ARCHED.TOPFORM.METHOD", "Method for calculating top formwork of arched beam", "Choose whether to calculate top formwork for arched beams", "0 Not calculate top formwork", "-", "Method", true);
            Row(rows, ref order, "Beam", "BEAM.CONNECTION.ALLOWANCE", "Fixing percentage of allowance for connections (%)", "Percentage allowance added for beam connection formwork", "2.5", "%", "Numeric", true);

            Row(rows, ref order, "Beam", "BEAM.SIDE", "Calculate side formwork", "Calculate exposed side faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Beam", "BEAM.BOTTOM", "Calculate bottom formwork", "Calculate soffit/bottom face", "Yes", "-", "Option", true);
            Row(rows, ref order, "Beam", "BEAM.TOP", "Calculate top formwork", "Do not calculate top face", "No", "-", "Option", false);
            Row(rows, ref order, "Beam", "BEAM.DEDUCT.FOUN", "Deduct foundation intersection", "Deduct foundation contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Beam", "BEAM.DEDUCT.COL", "Deduct column intersection", "Deduct column contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Beam", "BEAM.DEDUCT.WALL", "Deduct wall intersection", "Deduct wall contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Beam", "BEAM.DEDUCT.FLOOR", "Deduct floor/slab intersection", "Deduct slab contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Beam", "BEAM.DEDUCT.BEAM", "Deduct beam-to-beam intersection", "Deduct overlapped beam formwork", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Beam", "BEAM.DEDUCT.OTHER", "Deduct other concrete/generic intersections", "Deduct generic concrete contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Beam", "BEAM.LENGTH", "Beam quantity length source", "Use analytical/location curve length; fallback to bounding box", "Location curve", "-", "Method", true);
        }

        private static void AddWall(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Wall", "WALL.STRUT.BOTTOM.PLANE", "Method for calculating bottom plane of strutting high", "Select floor, ground elevation, or wall-bottom reference used as the lower plane for wall high-support formwork", "0 Select floor for both flat wall and sloping wall", "-", "Method", true);
            Row(rows, ref order, "Wall", "WALL.STRUT.BOTTOM.FLOOR", "Principle of strutting high bottom plane selecting floor", "Select the floor slab reference when the wall bottom plane method uses floor selection", "0 Select the bottom elevation of current floor", "-", "Method", true);
            Row(rows, ref order, "Wall", "WALL.STRUT.TOP.PLANE", "Method for calculating top plane of strutting high", "Select floor, wall top, slab bottom, or beam-below-slab reference used as the upper plane for wall high-support formwork", "2 Select wall top elevation for flat wall, select average wall top elevation for sloping wall", "-", "Method", true);
            Row(rows, ref order, "Wall", "WALL.STRUT.JUDGE.START", "Starting height to judge strutting high (m)", "Height threshold used to determine whether wall high-support formwork applies", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Wall", "WALL.STRUT.CALC.START", "Starting height to calculate strutting high (m)", "Height threshold used as the starting point for wall high-support quantities", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Wall", "WALL.STRUT.MAX.STAGES", "Maximum number of stages for strutting high", "Maximum stage count for wall high-support formwork", "10", "stage", "Numeric", true);
            Row(rows, ref order, "Wall", "WALL.STRUT.METHOD", "Method for calculating strutting high", "Choose whether wall high-support formwork is measured by total quantity, separate stages, classified stages, or skipped", "3 Not calculate strutting high", "-", "Method", true);
            Row(rows, ref order, "Wall", "WALL.STRUT.STAGE.HEIGHT", "Height of stage for strutting high (m)", "Vertical height of each wall high-support stage", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Wall", "WALL.HORIZONTAL.PROJECTION.TOP.SLOPE", "Provide formwork to top of horizontal projection if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers top formwork for horizontal projection", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Wall", "WALL.EDGE.METHOD", "Method for calculating edge and break formwork", "Choose area-only measurement or staged length/area measurement for wall edge and break formwork", "1 Calculate in stages: calculate by length if the width is less than the set value; otherwise, calculate by area", "-", "Method", true);
            Row(rows, ref order, "Wall", "WALL.EDGE.CONDITION", "Condition for calculating edge and break formwork in stages (formwork width (m) <=)", "Width threshold for staged wall edge and break formwork", "1.000", "m", "Numeric", true);
            Row(rows, ref order, "Wall", "WALL.EDGE.SEGMENT", "Segmentation standard of edge and break formwork", "Open or select the segmentation standard for wall edge and break formwork", "Segmentation standard of edge and break formwork", "-", "Segmentation", true);
            Row(rows, ref order, "Wall", "WALL.OPENING.EDGE.CONDITION", "Condition for calculating side formwork of opening as edge and break formwork", "Opening-area condition for treating opening side formwork as edge and break formwork", "Area (m2) >5.000", "-", "Condition", true);
            Row(rows, ref order, "Wall", "WALL.OPENING.ADDING.SIDE.METHOD", "Method for calculating formwork area adding side area of opening", "Choose whether opening side areas are added to the wall formwork area", "0 Add area of all sides of opening", "-", "Method", true);

            Row(rows, ref order, "Wall", "WALL.SIDE", "Calculate side formwork", "Calculate both exposed wall faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Wall", "WALL.ENDCAP", "Wall end cap rule", "Remove joined wall end cap faces", "Joined ends excluded", "-", "Method", true);
            Row(rows, ref order, "Wall", "WALL.OPENING.DEDUCT", "Deduct opening area", "Deduct door/window/opening area from side formwork", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Wall", "WALL.OPENING.BOTTOM", "Calculate bottom face of opening", "Calculate bottom faces of openings", "Yes", "-", "Option", true);
            Row(rows, ref order, "Wall", "WALL.OPENING.SIDE", "Calculate side formwork of opening", "Area (m2) > 5.000", "5.000", "m2", "Condition", true);
            Row(rows, ref order, "Wall", "WALL.DEDUCT.BEAM", "Deduct beam intersection", "Deduct beam contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Wall", "WALL.DEDUCT.COL", "Deduct column intersection", "Deduct column contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Wall", "WALL.DEDUCT.FLOOR", "Deduct floor/slab intersection", "Deduct slab contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Wall", "WALL.DEDUCT.FOUN", "Deduct foundation intersection", "Deduct foundation contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Wall", "WALL.DEDUCT.WALL", "Deduct wall-to-wall intersection", "Deduct joined/overlapped wall contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Wall", "WALL.DEDUCT.OTHER", "Deduct other concrete/generic intersections", "Deduct generic concrete contact area", "Yes", "-", "Deduction", true);
        }

        private static void AddSlab(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Slab", "SLAB.STRUT.BOTTOM", "Method for calculating bottom plane of strutting high", "Select the lower elevation reference used for slab high-support formwork", "1 Select slab top elevation if slab exists (in current floor and lower floor), select floor bottom elevation of the lower floor if no slab exists", "-", "Method", true);
            Row(rows, ref order, "Slab", "SLAB.STRUT.TOP", "Method for calculating top plane of strutting high", "Select the upper slab elevation reference used for slab high-support formwork", "1 Select slab bottom elevation for flat slab, select maximum slab bottom elevation for sloping slab", "-", "Method", true);
            Row(rows, ref order, "Slab", "SLAB.STRUT.JUDGE", "Starting height to judge strutting high (m)", "Height threshold used to determine whether slab high-support formwork applies", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Slab", "SLAB.STRUT.START", "Starting height to calculate strutting high (m)", "Height threshold used as the starting point for slab high-support quantities", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Slab", "SLAB.STRUT.MAXSTAGE", "Maximum number of stages for strutting high", "Maximum stage count for slab high-support formwork", "10", "stage", "Numeric", true);
            Row(rows, ref order, "Slab", "SLAB.STRUT.SOFFIT", "Method for calculating soffit formwork for strutting high", "Choose whether slab soffit high-support formwork is measured by total quantity, separate stages, or skipped", "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)", "-", "Method", true);
            Row(rows, ref order, "Slab", "SLAB.STRUT.EDGE", "Method for calculating edge and break formwork for strutting high", "Choose whether slab edge and break high-support formwork is measured by total quantity, separate stages, or skipped", "2 Not calculate strutting high", "-", "Method", true);
            Row(rows, ref order, "Slab", "SLAB.STRUT.TOPFWK", "Method for calculating top formwork for strutting high", "Choose whether slab top high-support formwork is measured by total quantity, separate stages, or skipped", "2 Not calculate strutting high", "-", "Method", true);
            Row(rows, ref order, "Slab", "SLAB.STRUT.STAGEHEIGHT", "Height of stage for strutting high (m)", "Vertical height of each slab high-support stage", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Slab", "SLAB.TOP.SLOPE", "Provide formwork to top of slab if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers top formwork for slab", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Slab", "SLAB.TOP.ARCHED", "Method for calculating top formwork of arched/spherical slab", "Choose whether top formwork is measured for arched or spherical slabs", "0 Not calculate top formwork", "-", "Method", true);
            Row(rows, ref order, "Slab", "SLAB.EDGE.METHOD", "Method for calculating edge and break formwork", "Choose area-only measurement or staged length/area measurement for slab edge and break formwork", "1 Calculate in stages: calculate by length if the height is less than the set value; otherwise, calculate by area", "-", "Method", true);
            Row(rows, ref order, "Slab", "SLAB.EDGE.STARTWIDTH", "Starting width to calculate edge and break formwork in stages", "Starting width for slab edge and break staged measurement", "1.000", "m", "Numeric", true);
            Row(rows, ref order, "Slab", "SLAB.EDGE.SEGMENT", "Segmentation standard of edge and break formwork", "Open or select the segmentation standard for slab edge and break formwork", "Segmentation standard of edge and break formwork", "-", "Segmentation", true);
            Row(rows, ref order, "Slab", "SLAB.OPENING.SIDE", "Condition for calculating side formwork of opening as side formwork", "Opening-area condition for slab opening side formwork", "Area (m2) >5.000", "m2", "Condition", true);
            Row(rows, ref order, "Slab", "SLAB.SLOPE.SOFFIT", "Method for calculating formwork to sloping surface to soffit of variable cross-section of slab", "Choose how sloping soffit formwork is measured and incorporated into soffit formwork", "1 Calculate in stages: calculate by length if less than the set value; otherwise, calculate by area, and incorporate its quantity into that of soffit formwork", "-", "Method", true);

            Row(rows, ref order, "Slab", "SLAB.EDGE.SEGMENT.1", "Segmentation standard of edge and break formwork", "Edge and break formwork", "0.250", "m", "Segmentation", true);
            Row(rows, ref order, "Slab", "SLAB.EDGE.SEGMENT.2", "Segmentation standard of edge and break formwork", "Edge and break formwork", "0.500", "m", "Segmentation", true);
            Row(rows, ref order, "Slab", "SLAB.EDGE.SEGMENT.3", "Segmentation standard of edge and break formwork", "Edge and break formwork", "1.000", "m", "Segmentation", true);
            Row(rows, ref order, "Slab", "SLAB.EDGE.SEGMENT.THEREAFTER", "And thereafter in m stages", "Edge and break formwork", "0.500", "m", "Numeric", true);
            Row(rows, ref order, "Slab", "SLAB.OPENING.SIDE.GIRTH", "Girth condition for opening side formwork", "Opening side formwork", "", "m", "Condition", false);
            Row(rows, ref order, "Slab", "SLAB.OPENING.SIDE.AREA", "Area condition for opening side formwork", "Opening side formwork", "5.000", "m2", "Condition", true);
            Row(rows, ref order, "Slab", "SLAB.OPENING.SIDE.VOLUME", "Volume condition for opening side formwork", "Opening side formwork", "", "m3", "Condition", false);

            Row(rows, ref order, "Slab", "SLAB.SIDE", "Calculate slab edge/break side formwork", "Calculate exposed slab edge and break faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Slab", "SLAB.BOTTOM", "Calculate slab bottom formwork", "Calculate soffit/bottom face", "Yes", "-", "Option", true);
            Row(rows, ref order, "Slab", "SLAB.DEDUCT.BEAM", "Deduct beam intersection", "Deduct beam contact area from slab formwork", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Slab", "SLAB.DEDUCT.FOUN", "Deduct foundation intersection", "Deduct foundation contact area from slab formwork", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Slab", "SLAB.DEDUCT.COL", "Deduct column intersection", "Deduct column contact area from slab formwork", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Slab", "SLAB.DEDUCT.WALL", "Deduct wall intersection", "Deduct wall contact area from slab formwork", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Slab", "SLAB.DEDUCT.SLAB", "Deduct slab-to-slab intersection", "Deduct overlapping slab contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Slab", "SLAB.DEDUCT.STAIR", "Deduct straight flight intersection", "Deduct stair or straight-flight contact area from slab soffit formwork", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Slab", "SLAB.DEDUCT.OTHER", "Deduct other concrete/generic intersections", "Deduct generic concrete contact area", "Yes", "-", "Deduction", true);
        }

        private static void AddKerbAndOthers(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Kerb", "KERB.SIDE", "Calculate kerb side formwork", "Calculate exposed vertical faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Kerb", "KERB.TOP", "Calculate kerb top formwork", "Do not calculate top face unless selected", "No", "-", "Option", false);
            Row(rows, ref order, "Kerb", "KERB.DEDUCT.WALL", "Deduct wall/column intersections", "Deduct contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Kerb", "KERB.LENGTH", "Kerb length source", "Use model curve length; fallback to solid perimeter", "Model length", "-", "Method", true);
            Row(rows, ref order, "Others", "OTHER.SIDE", "Calculate side formwork for other concrete", "Calculate exposed vertical faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Others", "OTHER.BOTTOM", "Calculate bottom formwork for other concrete", "Calculate soffit/bottom face", "Yes", "-", "Option", true);
            Row(rows, ref order, "Others", "OTHER.DEDUCT.STRUCTURE", "Deduct structural intersections", "Deduct foundation/beam/column/wall/slab contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Others", "OTHER.CLASSIFY", "Classification source", "Use category/type, then CBIM BOQ mapping", "Type mapping", "-", "Method", true);
        }

        private static void AddFinishRules(List<QsMeasurementSettingRow> rows, ref int order)
        {
            AddWallFinish(rows, ref order);
            AddCeilingFinish(rows, ref order);
            AddSuspendedCeiling(rows, ref order);
            AddFloorFinish(rows, ref order);
            AddFinish(rows, ref order, "Waterproof", "WP", "Waterproof area", "Use host face plus configured upturn height");
            Row(rows, ref order, "Waterproof", "WP.UPTURN", "Waterproof upturn height", "Add perimeter upturn area by configured height", "0.300", "m", "Numeric", true);
        }

        private static void AddWallFinish(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Wall Finish", "WF.INT.BOTTOM", "Method for calculating bottom elevation of finish to interior wall finish", "Select the lower elevation reference for standard interior wall finish", "1 Calculate height from floor", "-", "Method", true);
            Row(rows, ref order, "Wall Finish", "WF.INT.TOP", "Method for calculating top elevation of finish to interior wall finish", "Select the upper elevation reference for standard interior wall finish", "0 If with suspended ceiling, select suspended ceiling bottom elevation + set value; otherwise, select slab bottom elevation", "-", "Method", true);
            Row(rows, ref order, "Wall Finish", "WF.SUSPENDED.SETVALUE", "Set value above suspended ceiling (mm)", "Vertical set value added above the suspended ceiling bottom elevation", "50", "mm", "Numeric", true);
            Row(rows, ref order, "Wall Finish", "WF.EXT.METHOD", "Method for calculating finish to exterior wall finish", "Select the elevation reference for exterior wall finish", "0 If with basement, calculate from floor bottom elevation; if without basement, calculate from ground elevation for first floor, and calculate from floor bottom elevation for other floors", "-", "Method", true);
            Row(rows, ref order, "Wall Finish", "WF.CUSTOM.SHOW", "Show custom finish to wall finish", "Show additional custom wall-finish layer settings", "0 No", "-", "Option", true);
            Row(rows, ref order, "Wall Finish", "WF.CUSTOM.LAYERS", "Number of custom finish layers to interior wall finish", "Number of custom finish layers applied to the interior wall finish", "1", "-", "Numeric", true);
            Row(rows, ref order, "Wall Finish", "WF.CUSTOM.INT.BOTTOM", "Method for calculating bottom elevation of custom finish to interior wall finish", "Select the lower elevation reference for custom interior wall finish", "0 Calculate height from skirting", "-", "Method", true);
            Row(rows, ref order, "Wall Finish", "WF.CUSTOM.INT.TOP", "Method for calculating top elevation of custom finish to interior wall finish", "Select the upper elevation reference for custom interior wall finish", "0 If with suspended ceiling, select suspended ceiling bottom elevation + set value; otherwise, select slab bottom elevation", "-", "Method", true);
            Row(rows, ref order, "Wall Finish", "WF.CUSTOM.SUSPENDED.SETVALUE", "Set value above suspended ceiling (mm)", "Vertical set value added above the suspended ceiling bottom elevation for custom finish", "50", "mm", "Numeric", true);
            Row(rows, ref order, "Wall Finish", "WF.CUSTOM.EXT.METHOD", "Method for calculating custom finish to exterior wall finish", "Select the elevation reference for custom exterior wall finish", "0 If with basement, calculate from floor bottom elevation; if without basement, calculate from ground elevation for first floor, and calculate from floor bottom elevation for other floors", "-", "Method", true);
            Row(rows, ref order, "Wall Finish", "WF.TILE.WASTE.MODE", "Tile waste calculation mode", "Select tile waste priority mode", "0 Aesthetics first", "-", "Method", true);
            Row(rows, ref order, "Wall Finish", "WF.TILE.WASTE.CUT", "Waste generated per cut for tile (mm)", "Waste allowance generated by each tile cut", "5", "mm", "Numeric", true);

            AddFinish(rows, ref order, "Wall Finish", "WF", "Wall finish area", "Use wall face area after opening deduction");
        }

        private static void AddCeilingFinish(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Ceiling Finish", "CF.STRUT.BOTTOM", "Method for calculating bottom plane of strutting high", "Select slab/floor reference used as the lower plane for ceiling-finish high-support measurement", "1 Select slab top elevation if slab exists (in current floor and lower floor), select floor bottom elevation of the lower floor if no slab exists", "-", "Method", true);
            Row(rows, ref order, "Ceiling Finish", "CF.STRUT.TOP", "Method for calculating top plane of strutting high", "Select slab bottom reference used as the upper plane for ceiling-finish high-support measurement", "1 Select slab bottom elevation for flat ceiling finish, select average slab bottom elevation for sloping ceiling finish", "-", "Method", true);
            Row(rows, ref order, "Ceiling Finish", "CF.STRUT.JUDGE", "Starting height to judge strutting high (m)", "Height threshold used to determine whether ceiling-finish high-support measurement applies", "3.500", "m", "Numeric", true);
            Row(rows, ref order, "Ceiling Finish", "CF.STRUT.START", "Starting height to calculate strutting high (m)", "Height threshold used as the starting point for ceiling-finish high-support quantities", "3.500", "m", "Numeric", true);
            Row(rows, ref order, "Ceiling Finish", "CF.STRUT.MAXSTAGE", "Maximum number of stages for strutting high", "Maximum stage count for ceiling-finish high-support measurement", "10", "stage", "Numeric", true);
            Row(rows, ref order, "Ceiling Finish", "CF.STRUT.METHOD", "Method for calculating strutting high", "Choose whether ceiling-finish high-support measurement is total, staged, or skipped", "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)", "-", "Method", true);
            Row(rows, ref order, "Ceiling Finish", "CF.STRUT.STAGEHEIGHT", "Height of stage for strutting high (m)", "Vertical height of each ceiling-finish high-support stage", "1.500", "m", "Numeric", true);

            AddFinish(rows, ref order, "Ceiling Finish", "CF", "Ceiling finish area", "Use room/floor bottom projection");
        }

        private static void AddSuspendedCeiling(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Suspended Ceiling", "SC.VERTICAL.METHOD", "Method for calculating suspended ceiling to vertical surface", "Choose area-only measurement or staged length/area measurement for the vertical suspended-ceiling return surface", "2 Calculate in stages: calculate by area", "-", "Method", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.VERTICAL.CONDITION", "Condition for calculating suspended ceiling to vertical surface in stages (height (m) <=)", "Height threshold for staged vertical suspended-ceiling surface measurement", "0.500", "m", "Numeric", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.VERTICAL.SEGMENT", "Segmentation standard of suspended ceiling to vertical surface", "Open or select the height segmentation standard for suspended ceiling to vertical surface", "Segmentation standard of suspended ceiling to vertical surface", "-", "Segmentation", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.STRUT.BOTTOM", "Method for calculating bottom plane of strutting high", "Select slab/floor reference used as the lower plane for suspended-ceiling high-support measurement", "1 Select slab top elevation if slab exists (in current floor and lower floor), select floor bottom elevation of the lower floor if no slab exists", "-", "Method", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.STRUT.TOP", "Method for calculating top plane of strutting high", "Select the upper reference used for suspended-ceiling high-support measurement", "0 Select bottom elevation of suspended ceiling", "-", "Method", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.STRUT.JUDGE", "Starting height to judge strutting high (m)", "Height threshold used to determine whether suspended-ceiling high-support measurement applies", "3.500", "m", "Numeric", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.STRUT.START", "Starting height to calculate strutting high (m)", "Height threshold used as the starting point for suspended-ceiling high-support quantities", "3.500", "m", "Numeric", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.STRUT.MAXSTAGE", "Maximum number of stages for strutting high", "Maximum stage count for suspended-ceiling high-support measurement", "10", "stage", "Numeric", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.STRUT.METHOD", "Method for calculating strutting high", "Choose whether suspended-ceiling high-support measurement is total, staged, or skipped", "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)", "-", "Method", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.STRUT.STAGEHEIGHT", "Height of stage for strutting high (m)", "Vertical height of each suspended-ceiling high-support stage", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.AREA.METHOD", "Method for calculating area of suspended ceiling", "Choose area-only or staged area measurement for suspended-ceiling area", "0 Not calculate in stages: calculate by area", "-", "Method", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.AREA.CONDITION", "Condition for calculating area of suspended ceiling in stages (depth (m) <=)", "Depth threshold for staged suspended-ceiling area measurement", "0.500", "m", "Numeric", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.AREA.SOFFIT", "Principle of suspended ceiling area selecting structural soffit", "Select the structural soffit reference used to calculate suspended-ceiling area", "0 If there are slabs in the current floor, select the bottom elevation of top slab with shortest distance from the soffit of suspended ceiling; otherwise, select the top elevation of current floor", "-", "Method", true);
            Row(rows, ref order, "Suspended Ceiling", "SC.AREA.SEGMENT", "Segmentation standard of suspended ceiling", "Open or select the depth segmentation standard for suspended ceiling area", "Segmentation standard of suspended ceiling", "-", "Segmentation", true);

            AddFinish(rows, ref order, "Suspended Ceiling", "SC", "Suspended ceiling area", "Use ceiling object area; fallback to room boundary");
        }

        private static void AddFloorFinish(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Floor Finish", "FF.VERTICAL.METHOD", "Method for calculating floor finish to vertical surface", "Choose area-only measurement or staged length/area measurement for the vertical floor-finish return surface", "0 Not calculate in stages: calculate by area", "-", "Method", true);
            Row(rows, ref order, "Floor Finish", "FF.VERTICAL.CONDITION", "Condition for calculating floor finish to vertical surface in stages (height (m) <=)", "Height threshold for staged vertical floor-finish surface measurement", "0.300", "m", "Numeric", true);
            Row(rows, ref order, "Floor Finish", "FF.VERTICAL.SEGMENT", "Segmentation standard of floor finish to vertical surface", "Open or select the height segmentation standard for floor finish to vertical surface", "Segmentation standard of floor finish to vertical surface", "-", "Segmentation", true);
            Row(rows, ref order, "Floor Finish", "FF.TILE.WASTE.MODE", "Tile waste calculation mode", "Select tile waste priority mode", "0 Aesthetics first", "-", "Method", true);
            Row(rows, ref order, "Floor Finish", "FF.TILE.WASTE.CUT", "Waste generated per cut for tile (mm)", "Waste allowance generated by each tile cut", "5", "mm", "Numeric", true);

            AddFinish(rows, ref order, "Floor Finish", "FF", "Floor finish area", "Use room/floor top projection");
        }

        private static void AddFinish(List<QsMeasurementSettingRow> rows, ref int order, string category, string prefix, string title, string basis)
        {
            Row(rows, ref order, category, prefix + ".AREA", title, basis, "Yes", "-", "Area", true);
            Row(rows, ref order, category, prefix + ".OPENING", "Deduct openings", "Deduct openings above threshold", "Yes", "-", "Deduction", true);
            Row(rows, ref order, category, prefix + ".OPENING.MIN", "Opening deduction threshold", "Minimum opening area to deduct", "0.500", "m2", "Condition", true);
            Row(rows, ref order, category, prefix + ".RETURN", "Calculate opening returns/reveals", "Calculate side return finish around openings", "No", "-", "Option", false);
            Row(rows, ref order, category, prefix + ".ROOM", "Room/level grouping", "Group by level, room, type, then BOQ code", "Level + Room", "-", "Classification", true);
        }

        private static void AddExtendedStructureRules(List<QsMeasurementSettingRow> rows, ref int order)
        {
            Row(rows, ref order, "Lintel", "LINTEL.CANTILEVER.MAX", "Maximum length of cantilever of lintel (mm)", "Maximum cantilever length threshold for lintel measurement", "0", "mm", "Numeric", true);
            Row(rows, ref order, "Lintel", "LINTEL.ARCHED.TOPFORM.METHOD", "Method for calculating top formwork of arched lintel", "Choose whether to calculate top formwork for arched lintels", "0 Not calculate top formwork", "-", "Method", true);
            Row(rows, ref order, "Lintel", "LINTEL.SIDE", "Calculate lintel side formwork", "Calculate exposed side and bottom faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Lintel", "LINTEL.BOTTOM", "Calculate lintel bottom formwork", "Calculate soffit/bottom face", "Yes", "-", "Option", true);
            Row(rows, ref order, "Lintel", "LINTEL.DEDUCT.WALL", "Deduct wall overlap", "Deduct embedded wall contact area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Drop Panel", "DROP.STRUT.BOTTOM.PLANE", "Method for calculating bottom plane of strutting high", "Select floor or ground reference used as the lower plane for drop-panel high-support formwork", "1 Select ground elevation or floor", "-", "Method", true);
            Row(rows, ref order, "Drop Panel", "DROP.STRUT.BOTTOM.FLOOR", "Principle of strutting high bottom plane selecting floor", "Select the floor slab reference when the drop-panel bottom plane method uses floor selection", "0 Select the bottom elevation of current floor", "-", "Method", true);
            Row(rows, ref order, "Drop Panel", "DROP.STRUT.TOP.PLANE", "Method for calculating top plane of strutting high", "Select drop-panel top reference used as the upper plane for high-support formwork", "1 Select the value of drop panel top elevation minus slab thickness", "-", "Method", true);
            Row(rows, ref order, "Drop Panel", "DROP.STRUT.JUDGE.START", "Starting height to judge strutting high (m)", "Height threshold used to determine whether drop-panel high-support formwork applies", "3.500", "m", "Numeric", true);
            Row(rows, ref order, "Drop Panel", "DROP.STRUT.CALC.START", "Starting height to calculate strutting high (m)", "Height threshold used as the starting point for drop-panel high-support quantities", "3.500", "m", "Numeric", true);
            Row(rows, ref order, "Drop Panel", "DROP.STRUT.MAX.STAGES", "Maximum number of stages for strutting high", "Maximum stage count for drop-panel high-support formwork", "10", "stage", "Numeric", true);
            Row(rows, ref order, "Drop Panel", "DROP.STRUT.METHOD", "Method for calculating strutting high", "Choose whether drop-panel high-support formwork is measured by total quantity, separate stages, or skipped", "0 Not calculate strutting high in stages: calculate total quantities", "-", "Method", true);
            Row(rows, ref order, "Drop Panel", "DROP.STRUT.STAGE.HEIGHT", "Height of stage for strutting high (m)", "Vertical height of each drop-panel high-support stage", "1.500", "m", "Numeric", true);
            Row(rows, ref order, "Drop Panel", "DROP.SOFFIT", "Calculate drop panel soffit formwork", "Calculate bottom and exposed side faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Drop Panel", "DROP.DEDUCT.SLAB", "Deduct slab intersection", "Deduct top contact with slab", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Drop Panel", "DROP.HEIGHT.SEGMENT", "Height segmentation standard", "<=0.250; <=0.500; <=1.000; thereafter 0.500", "Profile", "m", "Segmentation", true);
            Row(rows, ref order, "Eave", "EAVE.TOP.SLOPE", "Provide formwork to top of eave if the surface slopes from the horizontal (degree) >=", "Minimum surface slope that triggers eave top formwork", "15", "deg", "Numeric", true);
            Row(rows, ref order, "Eave", "EAVE.BOTTOM", "Calculate eave bottom formwork", "Calculate soffit area", "Yes", "-", "Option", true);
            Row(rows, ref order, "Eave", "EAVE.EDGE", "Calculate eave edge formwork", "Calculate exposed edge/break faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Staircase", "STAIR.SIDE.METHOD", "Method for calculating side formwork", "Choose area-only measurement or staged length/area measurement for staircase side formwork", "1 Calculate in stages: calculate by length if the height is less than the set value; otherwise, calculate by area", "-", "Method", true);
            Row(rows, ref order, "Staircase", "STAIR.SIDE.CONDITION", "Condition for calculating side formwork in stages (formwork height (m) <=)", "Height threshold for staged staircase side formwork measurement", "1.000", "m", "Numeric", true);
            Row(rows, ref order, "Staircase", "STAIR.SIDE.SEGMENT", "Segmentation standard of side formwork", "Open or select the height segmentation standard for staircase side formwork", "Segmentation standard of side formwork", "-", "Segmentation", true);
            Row(rows, ref order, "Staircase", "STAIR.PAINTING", "Calculate stair painting faces", "TAS stair method: calculate exposed side/riser faces and bottom/soffit faces, excluding top/tread walking surfaces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Staircase", "STAIR.BOTTOM", "Legacy separate staircase bottom formwork", "TAS stair painting method includes underside in the painting bottom/soffit bucket", "No", "-", "Legacy", false);
            Row(rows, ref order, "Staircase", "STAIR.TOP", "Legacy staircase upper inclined face", "TAS stair painting method does not include top/tread formwork", "No", "-", "Legacy", false);
            Row(rows, ref order, "Staircase", "STAIR.DEDUCT.BEAM", "Deduct beam intersection", "Deduct intersecting beam area", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Staircase", "STAIR.DEDUCT.OTHER", "Deduct other structure intersections", "Deduct foundation/floor/wall/column/generic contacts", "Yes", "-", "Deduction", true);
            Row(rows, ref order, "Staircase", "STAIR.EDGE.SEGMENT", "Stair side segmentation standard", "Calculate by length below threshold; otherwise by area", "Profile", "m", "Segmentation", true);
            Row(rows, ref order, "Roof", "ROOF.WP.VERTICAL.METHOD", "Method for calculating waterproof to vertical surface", "Choose area-only measurement or staged length/area measurement for waterproof to vertical surface", "0 Not calculate in stages: calculate by area", "-", "Method", true);
            Row(rows, ref order, "Roof", "ROOF.WP.VERTICAL.CONDITION", "Condition for calculating waterproof to vertical surface in stages (height (m) <=)", "Height threshold for staged waterproof to vertical surface measurement", "0.300", "m", "Numeric", true);
            Row(rows, ref order, "Roof", "ROOF.WP.VERTICAL.SEGMENT", "Segmentation standard of waterproof to vertical surface", "Open or select the height segmentation standard for waterproof to vertical surface", "Segmentation standard of waterproof to vertical surface", "-", "Segmentation", true);
            Row(rows, ref order, "Roof", "ROOF.BOTTOM", "Calculate roof soffit formwork", "Calculate bottom face", "Yes", "-", "Option", true);
            Row(rows, ref order, "Roof", "ROOF.TOP.SLOPE", "Calculate roof top sloping formwork", "Calculate top face when slope threshold is reached", "15", "deg", "Condition", true);
            Row(rows, ref order, "Roof", "ROOF.EDGE", "Calculate roof edge/break formwork", "Calculate exposed edge faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Steel/Composite Slab", "COMP.BOTTOM", "Calculate composite slab bottom formwork", "Calculate underside deck/slab area", "Yes", "-", "Option", true);
            Row(rows, ref order, "Steel/Composite Slab", "COMP.EDGE", "Calculate composite slab edge formwork", "Calculate exposed slab edge/break faces", "Yes", "-", "Option", true);
            Row(rows, ref order, "Steel/Composite Slab", "COMP.OPENING", "Opening side formwork condition", "Area (m2) > 5.000", "5.000", "m2", "Condition", true);
            Row(rows, ref order, "Steel/Composite Slab", "COMP.SEGMENT", "Depth/height segmentation standard", "<=0.250; <=0.500; <=1.000; thereafter 0.500", "Profile", "m", "Segmentation", true);
        }

        private static void Row(
            List<QsMeasurementSettingRow> rows,
            ref int order,
            string category,
            string code,
            string description,
            string option,
            string value,
            string unit,
            string method,
            bool enabled)
        {
            rows.Add(new QsMeasurementSettingRow
            {
                SortOrder = order,
                Category = category,
                Code = code,
                Description = description,
                Option = option,
                Value = value,
                Unit = unit,
                Method = method,
                IsEnabled = enabled
            });
            order += 10;
        }
    }

    internal sealed class QsMeasurementSettingRow : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _category = "";
        private string _code = "";
        private string _description = "";
        private string _option = "";
        private string _value = "";
        private string _unit = "";
        private string _method = "";
        private List<string> _valueChoices = new List<string>();
        private List<string> _methodChoices = new List<string>();
        private int _displayIndex;
        private int _sortOrder;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsEnabled
        {
            get { return _isEnabled; }
            set { Set(ref _isEnabled, value, "IsEnabled"); }
        }

        public string Category
        {
            get { return _category; }
            set { Set(ref _category, value ?? "", "Category"); }
        }

        public string Code
        {
            get { return _code; }
            set
            {
                if (Set(ref _code, value ?? "", "Code"))
                {
                    RaisePropertyChanged("IsCubicostVisible");
                    RaisePropertyChanged("HasDetailSettings");
                }
            }
        }

        public string Description
        {
            get { return _description; }
            set { Set(ref _description, value ?? "", "Description"); }
        }

        public string Option
        {
            get { return _option; }
            set { Set(ref _option, value ?? "", "Option"); }
        }

        public string Value
        {
            get { return _value; }
            set
            {
                if (Set(ref _value, value ?? "", "Value"))
                {
                    QsMeasurementSettingsProfile.RefreshChoices(this);
                }
            }
        }

        public string Unit
        {
            get { return _unit; }
            set
            {
                if (Set(ref _unit, value ?? "", "Unit"))
                {
                    QsMeasurementSettingsProfile.RefreshChoices(this);
                }
            }
        }

        public string Method
        {
            get { return _method; }
            set
            {
                if (Set(ref _method, value ?? "", "Method"))
                {
                    QsMeasurementSettingsProfile.RefreshChoices(this);
                }
            }
        }

        public List<string> ValueChoices
        {
            get { return _valueChoices; }
            set
            {
                if (Set(ref _valueChoices, value ?? new List<string>(), "ValueChoices"))
                {
                    RaisePropertyChanged("ValueChoiceItems");
                }
            }
        }

        public List<string> MethodChoices
        {
            get { return _methodChoices; }
            set
            {
                if (Set(ref _methodChoices, value ?? new List<string>(), "MethodChoices"))
                {
                    RaisePropertyChanged("MethodChoiceItems");
                }
            }
        }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public List<QsMeasurementChoiceItem> ValueChoiceItems
        {
            get { return BuildChoiceItems(_valueChoices, true); }
        }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public List<QsMeasurementChoiceItem> MethodChoiceItems
        {
            get { return BuildChoiceItems(_methodChoices, false); }
        }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public bool IsCubicostVisible
        {
            get { return !IsCubicostHiddenRuleCode(Code); }
        }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public bool HasDetailSettings
        {
            get
            {
                return string.Equals(Code, "FOUN.SIDE.SETTINGS", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Code, "WALL.EDGE.SEGMENT", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Code, "WALL.OPENING.EDGE.CONDITION", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Code, "SLAB.EDGE.SEGMENT", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Code, "SLAB.OPENING.SIDE", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Code, "SC.VERTICAL.SEGMENT", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Code, "SC.AREA.SEGMENT", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Code, "FF.VERTICAL.SEGMENT", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Code, "STAIR.SIDE.SEGMENT", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Code, "ROOF.WP.VERTICAL.SEGMENT", StringComparison.OrdinalIgnoreCase);
            }
        }

        public int SortOrder
        {
            get { return _sortOrder; }
            set { Set(ref _sortOrder, value, "SortOrder"); }
        }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public int DisplayIndex
        {
            get { return _displayIndex; }
            set { Set(ref _displayIndex, value, "DisplayIndex"); }
        }

        private bool Set<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }

            return true;
        }

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private List<QsMeasurementChoiceItem> BuildChoiceItems(IEnumerable<string> choices, bool forValue)
        {
            var items = new List<QsMeasurementChoiceItem>();
            if (choices == null)
            {
                return items;
            }

            foreach (string choice in choices)
            {
                if (string.IsNullOrWhiteSpace(choice)) continue;
                items.Add(new QsMeasurementChoiceItem(choice.Trim(), DescribeChoice(choice, forValue)));
            }

            return items;
        }

        private string DescribeChoice(string choice, bool forValue)
        {
            string text = (choice ?? "").Trim();
            if (text.Length == 0)
            {
                return "";
            }

            if (!forValue)
            {
                if (string.Equals(text, "Option", StringComparison.OrdinalIgnoreCase)) return "Use as an include/exclude measurement switch.";
                if (string.Equals(text, "Deduction", StringComparison.OrdinalIgnoreCase)) return "Subtract overlapped, joined, opening, or contact area.";
                if (string.Equals(text, "Condition", StringComparison.OrdinalIgnoreCase)) return "Apply only when the rule threshold or test is satisfied.";
                if (string.Equals(text, "Method", StringComparison.OrdinalIgnoreCase)) return "Choose the measurement source or geometry strategy.";
                if (string.Equals(text, "Numeric", StringComparison.OrdinalIgnoreCase)) return "Use a numeric threshold, height, stage, angle, or limit.";
                if (string.Equals(text, "Elevation", StringComparison.OrdinalIgnoreCase)) return "Choose which elevation reference controls the quantity.";
                if (string.Equals(text, "Staged", StringComparison.OrdinalIgnoreCase)) return "Calculate by configured stages, similar to TAS high-support rules.";
                if (string.Equals(text, "Settings", StringComparison.OrdinalIgnoreCase)) return "Open nested TAS-style measurement settings.";
                if (string.Equals(text, "Area", StringComparison.OrdinalIgnoreCase)) return "Calculate an area-based finish or formwork item.";
                if (string.Equals(text, "Classification", StringComparison.OrdinalIgnoreCase)) return "Group or classify the result for BOQ takeoff.";
                if (string.Equals(text, "Segmentation", StringComparison.OrdinalIgnoreCase)) return "Split edge, height, or depth quantities into break ranges.";
                return "Custom condition type.";
            }

            if (string.Equals(text, "Yes", StringComparison.OrdinalIgnoreCase)) return "Calculate or apply this rule.";
            if (string.Equals(text, "No", StringComparison.OrdinalIgnoreCase)) return "Do not calculate or apply this rule.";
            if (string.Equals(text, "0 Not calculate working space", StringComparison.OrdinalIgnoreCase)) return "Do not add working-space allowance around the excavation.";
            if (string.Equals(text, "1 Calculate working space", StringComparison.OrdinalIgnoreCase)) return "Add working-space allowance around the excavation.";
            if (string.Equals(text, "0 Not calculate slope", StringComparison.OrdinalIgnoreCase)) return "Keep excavation sides vertical for this rule.";
            if (string.Equals(text, "1 Calculate slope", StringComparison.OrdinalIgnoreCase)) return "Add sloped excavation side allowance for this rule.";
            if (string.Equals(text, "Side Formwork Measurement Settings", StringComparison.OrdinalIgnoreCase)) return "Open the side formwork settings for foundation elements.";
            if (string.Equals(text, "0 Not calculate in stages: calculate by area", StringComparison.OrdinalIgnoreCase)) return "Calculate by area without staged length/area breakouts.";
            if (string.Equals(text, QsMeasurementSettingsProfile.FoundationSideDefaultMethodValue(), StringComparison.OrdinalIgnoreCase)) return "Use length below the set height, then area above it.";
            if (string.Equals(text, "2 Calculate in stages: calculate by area", StringComparison.OrdinalIgnoreCase)) return "Calculate staged quantities by area.";
            if (string.Equals(text, "0 Select floor", StringComparison.OrdinalIgnoreCase)) return "Use the selected floor elevation as the strutting-high bottom reference.";
            if (string.Equals(text, "1 Select ground elevation or floor", StringComparison.OrdinalIgnoreCase)) return "Use ground elevation where applicable, otherwise use the floor reference.";
            if (string.Equals(text, "2 Select floor if with basement; select ground elevation or floor if without basement", StringComparison.OrdinalIgnoreCase)) return "Use floor reference in basements; otherwise choose ground elevation or floor.";
            if (string.Equals(text, "3 Select soffit of entity", StringComparison.OrdinalIgnoreCase)) return "Use the soffit of the column entity as the bottom reference.";
            if (string.Equals(text, "0 Select the bottom elevation of current floor", StringComparison.OrdinalIgnoreCase)) return "Use the current floor bottom elevation directly.";
            if (text.StartsWith("1 If the bottom elevation of column below drop panel", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the higher slab top; otherwise choose the nearer slab below the drop panel.";
            if (text.StartsWith("2 If the bottom elevation of column below drop panel", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the higher slab top; otherwise choose the farther slab below the drop panel.";
            if (text.StartsWith("3 If the bottom elevation of column below drop panel", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the lower slab top; otherwise choose the nearer slab below the drop panel.";
            if (text.StartsWith("4 If the bottom elevation of column below drop panel", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the lower slab top; otherwise choose the farther slab below the drop panel.";
            if (text.StartsWith("1 If the bottom elevation of column", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the higher slab top; otherwise choose the nearer slab.";
            if (text.StartsWith("2 If the bottom elevation of column", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the higher slab top; otherwise choose the farther slab.";
            if (text.StartsWith("3 If the bottom elevation of column", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the lower slab top; otherwise choose the nearer slab.";
            if (text.StartsWith("4 If the bottom elevation of column", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the lower slab top; otherwise choose the farther slab.";
            if (string.Equals(text, "0 Select top elevation of column", StringComparison.OrdinalIgnoreCase)) return "Use the modeled top elevation of the column.";
            if (string.Equals(text, "1 Select the value of column top elevation minus slab thickness", StringComparison.OrdinalIgnoreCase)) return "Subtract slab thickness from the column top elevation.";
            if (string.Equals(text, "2 Select the value of column top elevation minus beam height", StringComparison.OrdinalIgnoreCase)) return "Subtract beam height from the column top elevation.";
            if (string.Equals(text, "0 Select top elevation of drop panel", StringComparison.OrdinalIgnoreCase)) return "Use the modeled top elevation of the drop panel.";
            if (string.Equals(text, "1 Select the value of drop panel top elevation minus slab thickness", StringComparison.OrdinalIgnoreCase)) return "Subtract slab thickness from the drop-panel top elevation.";
            if (string.Equals(text, "0 Not calculate strutting high in stages: calculate total quantities", StringComparison.OrdinalIgnoreCase)) return "Do not split by high-support stages; calculate total formwork quantity.";
            if (string.Equals(text, "1 Calculate strutting high in stages: calculate quantities of each stage separately (including basic stage)", StringComparison.OrdinalIgnoreCase)) return "Split high-support quantity and report each stage separately.";
            if (string.Equals(text, "2 Not calculate strutting high: calculate formwork in stages", StringComparison.OrdinalIgnoreCase)) return "Skip high-support quantity but keep staged formwork measurement.";
            if (string.Equals(text, "2 Not calculate strutting high", StringComparison.OrdinalIgnoreCase)) return "Do not calculate high-support formwork for this rule.";
            if (string.Equals(text, "3 Not calculate strutting high", StringComparison.OrdinalIgnoreCase)) return "Do not calculate high-support formwork for this column rule.";
            if (string.Equals(text, "0 Select slab top elevation for flat slab, select maximum slab top elevation for sloping slab", StringComparison.OrdinalIgnoreCase)) return "Use slab top elevation, taking the maximum slab top for sloping slabs.";
            if (string.Equals(text, "1 Select slab bottom elevation for flat slab, select maximum slab bottom elevation for sloping slab", StringComparison.OrdinalIgnoreCase)) return "Use slab bottom elevation, taking the maximum slab bottom for sloping slabs.";
            if (string.Equals(text, "2 Select slab top elevation for flat slab, select average slab top elevation for sloping slab", StringComparison.OrdinalIgnoreCase)) return "Use slab top elevation, taking the average slab top for sloping slabs.";
            if (string.Equals(text, "3 Select slab bottom elevation for flat slab, select average slab bottom elevation for sloping slab", StringComparison.OrdinalIgnoreCase)) return "Use slab bottom elevation, taking the average slab bottom for sloping slabs.";
            if (text.StartsWith("0 Select slab top elevation", StringComparison.OrdinalIgnoreCase)) return "Use slab top when available; otherwise use current floor bottom elevation.";
            if (text.StartsWith("1 Select slab top elevation", StringComparison.OrdinalIgnoreCase)) return "Use slab top when available; otherwise use lower floor bottom elevation.";
            if (text.StartsWith("2 Select slab top elevation", StringComparison.OrdinalIgnoreCase)) return "Use slab top across lower floors; otherwise use first floor bottom elevation.";
            if (text.StartsWith("0 Select beam top elevation", StringComparison.OrdinalIgnoreCase)) return "Use beam top elevation, taking the maximum top elevation for sloping beams.";
            if (text.StartsWith("1 Select beam bottom elevation", StringComparison.OrdinalIgnoreCase)) return "Use beam bottom elevation, taking the maximum bottom elevation for sloping beams.";
            if (text.StartsWith("2 Select beam top elevation", StringComparison.OrdinalIgnoreCase)) return "Use beam top elevation, taking the average top elevation for sloping beams.";
            if (text.StartsWith("3 Select beam bottom elevation", StringComparison.OrdinalIgnoreCase)) return "Use beam bottom elevation, taking the average bottom elevation for sloping beams.";
            if (text.StartsWith("4 Select the bottom elevation of slab intersecting with beam", StringComparison.OrdinalIgnoreCase)) return "Use intersecting slab bottom elevation, averaging it for sloping beams.";
            if (text.StartsWith("5 Select the bottom elevation of slab intersecting with beam", StringComparison.OrdinalIgnoreCase)) return "Use intersecting slab bottom elevation, taking the maximum for sloping beams.";
            if (string.Equals(text, "0 Not calculate top formwork", StringComparison.OrdinalIgnoreCase)) return "Do not calculate top formwork for arched or spherical elements.";
            if (string.Equals(text, "1 Calculate top formwork", StringComparison.OrdinalIgnoreCase)) return "Calculate top formwork for arched or spherical elements.";
            if (Unit != null && string.Equals(Unit.Trim(), "mm", StringComparison.OrdinalIgnoreCase) && string.Equals(Code, "LINTEL.CANTILEVER.MAX", StringComparison.OrdinalIgnoreCase)) return "Maximum lintel cantilever length in millimetres.";
            if (string.Equals(text, "0 Select floor for both flat wall and sloping wall", StringComparison.OrdinalIgnoreCase)) return "Use floor reference for both flat and sloping walls.";
            if (string.Equals(text, "1 Select ground elevation or floor for both flat wall and sloping wall", StringComparison.OrdinalIgnoreCase)) return "Use ground elevation where applicable, otherwise use floor reference.";
            if (text.StartsWith("2 For both flat wall and sloping wall", StringComparison.OrdinalIgnoreCase)) return "Use floor reference in basements; otherwise choose ground elevation or floor.";
            if (string.Equals(text, "3 Select floor for flat wall, select average wall bottom elevation for sloping wall", StringComparison.OrdinalIgnoreCase)) return "Use floor for flat walls and average wall-bottom elevation for sloping walls.";
            if (string.Equals(text, "4 Select ground elevation or floor for flat wall, select average wall bottom elevation for sloping wall", StringComparison.OrdinalIgnoreCase)) return "Use ground/floor reference for flat walls and average wall-bottom elevation for sloping walls.";
            if (text.StartsWith("5 For flat wall, select floor if with basement", StringComparison.OrdinalIgnoreCase)) return "Use basement-aware floor or ground reference for flat walls and average wall-bottom elevation for sloping walls.";
            if (text.StartsWith("1 If the bottom elevation of wall", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the higher slab top; otherwise choose the nearer slab.";
            if (text.StartsWith("2 If the bottom elevation of wall", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the higher slab top; otherwise choose the farther slab.";
            if (text.StartsWith("3 If the bottom elevation of wall", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the lower slab top; otherwise choose the nearer slab.";
            if (text.StartsWith("4 If the bottom elevation of wall", StringComparison.OrdinalIgnoreCase)) return "At slab contact choose the lower slab top; otherwise choose the nearer slab.";
            if (string.Equals(text, "0 Select floor top elevation", StringComparison.OrdinalIgnoreCase)) return "Use the top elevation of the floor reference.";
            if (string.Equals(text, "1 Select wall top elevation for flat wall, select maximum wall top elevation for sloping wall", StringComparison.OrdinalIgnoreCase)) return "Use wall top elevation, taking maximum wall top for sloping walls.";
            if (string.Equals(text, "2 Select wall top elevation for flat wall, select average wall top elevation for sloping wall", StringComparison.OrdinalIgnoreCase)) return "Use wall top elevation, taking average wall top for sloping walls.";
            if (string.Equals(text, "3 Select slab bottom elevation for flat wall, select maximum slab bottom elevation for sloping wall", StringComparison.OrdinalIgnoreCase)) return "Use slab bottom elevation, taking maximum slab bottom for sloping walls.";
            if (string.Equals(text, "4 Select slab bottom elevation for flat wall, select average slab bottom elevation for sloping wall", StringComparison.OrdinalIgnoreCase)) return "Use slab bottom elevation, taking average slab bottom for sloping walls.";
            if (text.StartsWith("5 Select minimum bottom elevation of beam below slab", StringComparison.OrdinalIgnoreCase)) return "Use the minimum beam-below-slab bottom elevation reference.";
            if (string.Equals(text, "2 Calculate strutting high in stages: classify by strutting high height", StringComparison.OrdinalIgnoreCase)) return "Classify staged high-support quantities by high-support height.";
            if (string.Equals(text, "0 Add area of all sides of opening", StringComparison.OrdinalIgnoreCase)) return "Add all opening side areas to the wall formwork area.";
            if (string.Equals(text, "1 Add area of all sides except the soffit of opening", StringComparison.OrdinalIgnoreCase)) return "Add opening side areas except the soffit.";
            if (text.StartsWith("Area (m2) >", StringComparison.OrdinalIgnoreCase)) return "Opening side formwork applies above this area threshold.";
            if (string.Equals(text, "Segmentation standard of edge and break formwork", StringComparison.OrdinalIgnoreCase)) return "Use the configured edge and break segmentation standard.";
            if (string.Equals(text, "1 Calculate in stages: calculate by length if the width is less than the set value; otherwise, calculate by area", StringComparison.OrdinalIgnoreCase)) return "Use length below the set width, then area above it.";
            if (string.Equals(text, "1 Calculate in stages: calculate by length if the height is less than the set value; otherwise, calculate by area", StringComparison.OrdinalIgnoreCase)) return "Use length below the set height, then area above it.";
            if (string.Equals(text, "1 Calculate in stages: calculate by length if the height of vertical surface is less than the set value; otherwise, calculate by area", StringComparison.OrdinalIgnoreCase)) return "Use length below the staged vertical-surface height threshold, then area above it.";
            if (string.Equals(text, "1 Calculate in stages: calculate by length if the depth is less than the set value; otherwise, calculate by area", StringComparison.OrdinalIgnoreCase)) return "Use length below the staged suspended-ceiling depth threshold, then area above it.";
            if (string.Equals(text, "1 Calculate in stages: calculate by area", StringComparison.OrdinalIgnoreCase)) return "Calculate suspended-ceiling area in configured stages.";
            if (string.Equals(text, "Segmentation standard of suspended ceiling to vertical surface", StringComparison.OrdinalIgnoreCase)) return "Use the configured height segmentation standard for the vertical suspended-ceiling surface.";
            if (string.Equals(text, "Segmentation standard of suspended ceiling", StringComparison.OrdinalIgnoreCase)) return "Use the configured depth segmentation standard for suspended-ceiling area.";
            if (string.Equals(text, "Segmentation standard of floor finish to vertical surface", StringComparison.OrdinalIgnoreCase)) return "Use the configured height segmentation standard for the vertical floor-finish surface.";
            if (string.Equals(text, "Segmentation standard of side formwork", StringComparison.OrdinalIgnoreCase)) return "Use the configured height segmentation standard for staircase side formwork.";
            if (string.Equals(text, "Segmentation standard of waterproof to vertical surface", StringComparison.OrdinalIgnoreCase)) return "Use the configured height segmentation standard for waterproof to vertical surface.";
            if (string.Equals(text, "<=0.150; <=0.225; <=0.300; thereafter 0.075", StringComparison.OrdinalIgnoreCase)) return "Segment at 0.150, 0.225, and 0.300 m, then continue in 0.075 m stages.";
            if (string.Equals(text, "<=0.500; thereafter 0.500", StringComparison.OrdinalIgnoreCase)) return "Segment at 0.500 m, then continue in 0.500 m stages.";
            if (string.Equals(text, "0 Select bottom elevation of suspended ceiling", StringComparison.OrdinalIgnoreCase)) return "Use the suspended ceiling bottom elevation as the high-support top plane.";
            if (text.StartsWith("0 If there are slabs in the current floor, select the bottom elevation of top slab", StringComparison.OrdinalIgnoreCase)) return "Choose the nearest top slab soffit above the suspended ceiling; otherwise use the current floor top elevation.";
            if (string.Equals(text, "0 Not calculate formwork to sloping surface to soffit", StringComparison.OrdinalIgnoreCase)) return "Skip variable-section sloping soffit formwork.";
            if (text.StartsWith("0 Not calculate in stages: calculate by area, and incorporate", StringComparison.OrdinalIgnoreCase)) return "Calculate variable-section sloping soffit formwork by area and include it with soffit formwork.";
            if (text.StartsWith("1 Calculate in stages: calculate by length if less than the set value", StringComparison.OrdinalIgnoreCase)) return "Use length below the staged threshold, then area, and include it with soffit formwork.";
            if (text.StartsWith("2 Calculate in stages: calculate by area, and incorporate", StringComparison.OrdinalIgnoreCase)) return "Calculate staged sloping soffit formwork by area and include it with soffit formwork.";
            if (string.Equals(text, "2 Calculate in stages: calculate by area", StringComparison.OrdinalIgnoreCase)) return "Calculate staged formwork by area.";
            if (string.Equals(text, "0 Calculate height from skirting", StringComparison.OrdinalIgnoreCase)) return "Use the skirting height as the lower interior wall finish reference.";
            if (string.Equals(text, "1 Calculate height from floor", StringComparison.OrdinalIgnoreCase)) return "Use floor elevation as the lower interior wall finish reference.";
            if (string.Equals(text, "2 Select floor bottom elevation + set value", StringComparison.OrdinalIgnoreCase)) return "Use floor bottom elevation plus the configured set value.";
            if (text.StartsWith("0 If with suspended ceiling", StringComparison.OrdinalIgnoreCase)) return "Use suspended ceiling bottom plus set value when present; otherwise use slab bottom.";
            if (string.Equals(text, "1 Select slab bottom elevation", StringComparison.OrdinalIgnoreCase)) return "Use slab bottom elevation as the upper finish reference.";
            if (text.StartsWith("0 If with basement, calculate from floor bottom elevation", StringComparison.OrdinalIgnoreCase)) return "Use floor bottom in basements; otherwise use ground for first floor and floor bottom for upper floors.";
            if (string.Equals(text, "1 Calculate from ground elevation", StringComparison.OrdinalIgnoreCase)) return "Use ground elevation as the exterior finish reference.";
            if (string.Equals(text, "0 No", StringComparison.OrdinalIgnoreCase)) return "Do not show the additional custom wall finish rows.";
            if (string.Equals(text, "1 Yes", StringComparison.OrdinalIgnoreCase)) return "Show the additional custom wall finish rows.";
            if (string.Equals(text, "0 Aesthetics first", StringComparison.OrdinalIgnoreCase)) return "Prioritize tile layout appearance when calculating waste.";
            if (string.Equals(text, "1 Cost first", StringComparison.OrdinalIgnoreCase)) return "Prioritize cost saving when calculating tile waste.";
            if (string.Equals(text, "0 Select slab bottom elevation for flat ceiling finish, select maximum slab bottom elevation for sloping ceiling finish", StringComparison.OrdinalIgnoreCase)) return "Use slab bottom elevation, taking the maximum slab bottom for sloping ceiling finishes.";
            if (string.Equals(text, "1 Select slab bottom elevation for flat ceiling finish, select average slab bottom elevation for sloping ceiling finish", StringComparison.OrdinalIgnoreCase)) return "Use slab bottom elevation, taking the average slab bottom for sloping ceiling finishes.";
            if (string.Equals(text, "Revit solid", StringComparison.OrdinalIgnoreCase)) return "Read area from Revit solid faces first.";
            if (string.Equals(text, "Type width/depth", StringComparison.OrdinalIgnoreCase)) return "Use family/type dimensions as a fallback.";
            if (string.Equals(text, "Bounding box", StringComparison.OrdinalIgnoreCase)) return "Use element extents when precise geometry is unavailable.";
            if (string.Equals(text, "Location curve", StringComparison.OrdinalIgnoreCase)) return "Measure length from the model location curve.";
            if (string.Equals(text, "Analytical curve", StringComparison.OrdinalIgnoreCase)) return "Measure from analytical curve where available.";
            if (string.Equals(text, "Joined ends excluded", StringComparison.OrdinalIgnoreCase)) return "Remove joined wall end-cap faces.";
            if (string.Equals(text, "Include end caps", StringComparison.OrdinalIgnoreCase)) return "Keep exposed wall end-cap faces.";
            if (string.Equals(text, "Revit joined geometry", StringComparison.OrdinalIgnoreCase)) return "Let Revit joined geometry control end faces.";
            if (string.Equals(text, "Model length", StringComparison.OrdinalIgnoreCase)) return "Use modeled curve or host element length.";
            if (string.Equals(text, "Solid perimeter", StringComparison.OrdinalIgnoreCase)) return "Estimate length from solid perimeter.";
            if (string.Equals(text, "Type mapping", StringComparison.OrdinalIgnoreCase)) return "Classify by type name and mapped rules.";
            if (string.Equals(text, "Category", StringComparison.OrdinalIgnoreCase)) return "Classify by Revit category.";
            if (string.Equals(text, "CBIM BOQ mapping", StringComparison.OrdinalIgnoreCase)) return "Classify using CBIM BOQ mapping.";
            if (text.IndexOf("Room", StringComparison.OrdinalIgnoreCase) >= 0) return "Group finish quantity with room identity.";
            if (string.Equals(text, "Level only", StringComparison.OrdinalIgnoreCase)) return "Group by building level only.";
            if (string.Equals(text, "Type only", StringComparison.OrdinalIgnoreCase)) return "Group by type name only.";
            if (string.Equals(text, "BOQ code", StringComparison.OrdinalIgnoreCase)) return "Group by BOQ code.";
            if (string.Equals(text, "Profile", StringComparison.OrdinalIgnoreCase)) return "Use the configured segmentation profile.";
            if (string.Equals(text, "Custom", StringComparison.OrdinalIgnoreCase)) return "Type a custom measurement profile.";
            if (text.StartsWith("Option", StringComparison.OrdinalIgnoreCase)) return "TAS-style predefined calculation option.";

            string unit = (Unit ?? "").Trim();
            if (string.Equals(unit, "m2", StringComparison.OrdinalIgnoreCase)) return "Area threshold in square metres.";
            if (string.Equals(unit, "m", StringComparison.OrdinalIgnoreCase)) return "Length or height threshold in metres.";
            if (string.Equals(unit, "mm", StringComparison.OrdinalIgnoreCase)) return "Set value or waste allowance in millimetres.";
            if (string.Equals(unit, "deg", StringComparison.OrdinalIgnoreCase)) return "Slope angle threshold in degrees.";
            if (string.Equals(unit, "stage", StringComparison.OrdinalIgnoreCase)) return "Maximum or selected stage count.";
            if (string.Equals(unit, "%", StringComparison.OrdinalIgnoreCase)) return "Percentage allowance added to the measured quantity.";

            return "Rule value for this measurement setting.";
        }

        private static bool IsCubicostHiddenRuleCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            if (code.StartsWith("FOUN.SIDE.SETTING.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "FOUN.SIDE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "FOUN.TOP", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("FOUN.DEDUCT.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "COL.SIDE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "COL.TOPBOTTOM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "COL.SHAPE", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("COL.DEDUCT.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "BEAM.SIDE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "BEAM.BOTTOM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "BEAM.TOP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "BEAM.LENGTH", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("BEAM.DEDUCT.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (code.StartsWith("WALL.EDGE.SEGMENT.", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("WALL.OPENING.EDGE.CONDITION.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "WALL.SIDE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WALL.ENDCAP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WALL.OPENING.DEDUCT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WALL.OPENING.BOTTOM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WALL.OPENING.SIDE", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("WALL.DEDUCT.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (code.StartsWith("SLAB.EDGE.SEGMENT.", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("SLAB.OPENING.SIDE.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "SLAB.SIDE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "SLAB.BOTTOM", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("SLAB.DEDUCT.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "WF.AREA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WF.OPENING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WF.OPENING.MIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WF.RETURN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WF.ROOM", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "CF.AREA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "CF.OPENING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "CF.OPENING.MIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "CF.RETURN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "CF.ROOM", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (code.StartsWith("SC.VERTICAL.SEGMENT.", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("SC.AREA.SEGMENT.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "SC.AREA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "SC.OPENING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "SC.OPENING.MIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "SC.RETURN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "SC.ROOM", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "LINTEL.SIDE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "LINTEL.BOTTOM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "LINTEL.DEDUCT.WALL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "DROP.SOFFIT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "DROP.DEDUCT.SLAB", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "DROP.HEIGHT.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "EAVE.BOTTOM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "EAVE.EDGE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (code.StartsWith("STAIR.SIDE.SEGMENT.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "STAIR.PAINTING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "STAIR.BOTTOM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "STAIR.TOP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "STAIR.DEDUCT.BEAM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "STAIR.DEDUCT.OTHER", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "STAIR.EDGE.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (code.StartsWith("ROOF.WP.VERTICAL.SEGMENT.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "ROOF.BOTTOM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "ROOF.TOP.SLOPE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "ROOF.EDGE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (code.StartsWith("FF.VERTICAL.SEGMENT.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(code, "FF.AREA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "FF.OPENING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "FF.OPENING.MIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "FF.RETURN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "FF.ROOM", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }

    internal sealed class QsMeasurementChoiceItem
    {
        public QsMeasurementChoiceItem(string value, string description)
        {
            Value = value ?? "";
            Description = description ?? "";
        }

        public string Value { get; private set; }
        public string Description { get; private set; }

        public override string ToString()
        {
            return Value;
        }
    }
}
