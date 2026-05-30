using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace CamboBIM.Revit2024.Addin
{
    public partial class CamboBIMWindow
    {
        private void OnQsMeasurementRulesSyncSettingsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                int changed = SyncQsMeasurementRulesFromSettings();
                ShowStatus($"Measurement Rules synchronized from Measurement Settings: {changed} rule state(s) updated.");
            }
            catch (Exception ex)
            {
                ShowStatus("Measurement Rules sync failed: " + ex.Message);
            }
        }

        private int SyncQsMeasurementRulesFromSettings()
        {
            if (_qsMeasurementRulesProfile == null)
            {
                _qsMeasurementRulesProfile = QsMeasurementRulesProfile.CreateDefault();
            }

            if (_qsMeasurementSettingsProfile == null)
            {
                _qsMeasurementSettingsProfile = QsMeasurementSettingsProfile.CreateDefault();
            }

            _qsMeasurementRulesProfile.Normalize();
            _qsMeasurementSettingsProfile.Normalize();

            int changed = 0;
            _qsMeasurementRulesLoading = true;
            try
            {
                foreach (QsMeasurementRuleRow row in GetQsMeasurementRulesAllRows())
                {
                    bool nextEnabled = ResolveTasRuleEnabledFromSettings(row, _qsMeasurementSettingsProfile);
                    if (row.IsEnabled != nextEnabled)
                    {
                        row.IsEnabled = nextEnabled;
                        changed++;
                    }
                }

                string settingsName = _qsMeasurementSettingsProfile.ProfileName ?? "Measurement Settings";
                _qsMeasurementRulesProfile.ProfileName = "MHNK Measurement Rules - Cubicost TAS (" + settingsName + ")";
                if (QsMeasurementRuleProfileNameTextBox != null)
                {
                    QsMeasurementRuleProfileNameTextBox.Text = _qsMeasurementRulesProfile.ProfileName;
                }
            }
            finally
            {
                _qsMeasurementRulesLoading = false;
            }

            RefreshQsMeasurementRuleCategoryList();
            RefreshQsMeasurementRuleConditionList();
            ApplyQsMeasurementRulesFilter();
            MarkQsMeasurementRulesDirty();
            return changed;
        }

        private static bool ResolveTasRuleEnabledFromSettings(QsMeasurementRuleRow row, QsMeasurementSettingsProfile settings)
        {
            if (row == null || settings == null) return true;

            string code = row.Code ?? "";
            string category = row.Category ?? "";
            string method = row.Method ?? "";

            if (!IsTasCategoryEnabled(category, settings))
            {
                return false;
            }

            if (IsTasBaseRule(code, method))
            {
                return true;
            }

            if (code.StartsWith("EXC.", StringComparison.OrdinalIgnoreCase))
            {
                return IsAnySettingEnabled(settings, true, "EXC.WORKSPACE.TRENCH", "EXC.WORKSPACE.HEAVY", "EXC.WORKSPACE.PIT", "EXC.SLOPE.TRENCH", "EXC.SLOPE.HEAVY", "EXC.SLOPE.PIT");
            }

            if (code.StartsWith("RAFT.QTY.SIDE", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("PAD.QTY.SIDE", StringComparison.OrdinalIgnoreCase) ||
                code.EndsWith(".QTY.VERTICAL", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "FOUN.SIDE", true);
            }

            if (code.EndsWith(".QTY.TOP", StringComparison.OrdinalIgnoreCase))
            {
                if (code.StartsWith("PAD.", StringComparison.OrdinalIgnoreCase) ||
                    code.StartsWith("RAFT.", StringComparison.OrdinalIgnoreCase))
                {
                    return IsSettingEnabled(settings, "FOUN.TOP", true);
                }

                if (code.StartsWith("STAIR.", StringComparison.OrdinalIgnoreCase))
                {
                    return IsSettingEnabled(settings, "STAIR.TOP", false);
                }

                if (code.StartsWith("KERB.", StringComparison.OrdinalIgnoreCase))
                {
                    return IsSettingEnabled(settings, "KERB.TOP", false);
                }
            }

            if (code.StartsWith("COL.QTY.FORMWORK", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "COL.SIDE", true);
            }

            if (code.StartsWith("BEAM.QTY.FORMWORK.BASIC", StringComparison.OrdinalIgnoreCase))
            {
                return IsAnySettingEnabled(settings, true, "BEAM.SIDE", "BEAM.BOTTOM");
            }

            if (code.StartsWith("BEAM.QTY.FORMWORK.STAGE", StringComparison.OrdinalIgnoreCase))
            {
                return IsAnySettingEnabled(settings, true, "BEAM.SIDE", "BEAM.BOTTOM") &&
                       IsSettingEnabled(settings, "BEAM.STRUT.METHOD", true, "Not calculate strutting high");
            }

            if (code.StartsWith("WALL.QTY.FORMWORK", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "WALL.SIDE", true);
            }

            if (code.StartsWith("WALL.QTY.EDGE", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "WALL.EDGE.METHOD", true, "Not calculate");
            }

            if (code.StartsWith("SLAB.QTY.SOFFIT.BASIC", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "SLAB.BOTTOM", true);
            }

            if (code.StartsWith("SLAB.QTY.SOFFIT.STAGE", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "SLAB.BOTTOM", true) &&
                       IsSettingEnabled(settings, "SLAB.STRUT.SOFFIT", true, "Not calculate strutting high");
            }

            if (code.StartsWith("SLAB.QTY.EDGE", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "SLAB.SIDE", true) &&
                       IsSettingEnabled(settings, "SLAB.EDGE.METHOD", true, "Not calculate");
            }

            if (code.StartsWith("SOPEN.", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "SLAB.OPENING.SIDE", true);
            }

            if (code.StartsWith("KERB.QTY.FORMWORK.SIDE", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "KERB.SIDE", true);
            }

            if (code.StartsWith("KERB.QTY.FORMWORK.TOP", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "KERB.TOP", false);
            }

            if (code.StartsWith("OTHER.QTY.FORMWORK.SIDE", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "OTHER.SIDE", true);
            }

            if (code.StartsWith("OTHER.QTY.FORMWORK.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "OTHER.BOTTOM", true);
            }

            if (code.EndsWith(".QTY.OPENING", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, GetFinishSettingPrefix(code) + ".OPENING", true);
            }

            if (code.EndsWith(".QTY.RETURN", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, GetFinishSettingPrefix(code) + ".RETURN", false);
            }

            if (code.StartsWith("WP.QTY.UPTURN", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "WP.UPTURN", true);
            }

            if (code.StartsWith("DROP.QTY.STRUT", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "DROP.STRUT.METHOD", true, "Not calculate strutting high");
            }

            if (code.StartsWith("DROP.QTY.SOFFIT", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "DROP.SOFFIT", true);
            }

            if (code.StartsWith("EAVE.QTY.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "EAVE.BOTTOM", true);
            }

            if (code.StartsWith("EAVE.QTY.EDGE", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "EAVE.EDGE", true);
            }

            if (code.StartsWith("STAIR.QTY.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "STAIR.BOTTOM", false);
            }

            if (code.StartsWith("STAIR.QTY.PAINTING", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "STAIR.PAINTING", true) &&
                       IsSettingEnabled(settings, "STAIR.SIDE.METHOD", true, "Not calculate");
            }

            if (code.StartsWith("STAIR.QTY.SIDE", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "STAIR.PAINTING", true) &&
                       IsSettingEnabled(settings, "STAIR.SIDE.METHOD", true, "Not calculate");
            }

            if (code.StartsWith("LINTEL.QTY.FORMWORK.SIDE", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "LINTEL.SIDE", true);
            }

            if (code.StartsWith("LINTEL.QTY.FORMWORK.BOTTOM", StringComparison.OrdinalIgnoreCase))
            {
                return IsSettingEnabled(settings, "LINTEL.BOTTOM", true);
            }

            return true;
        }

        private static bool IsTasCategoryEnabled(string category, QsMeasurementSettingsProfile settings)
        {
            if (string.Equals(category, "Wall Finish", StringComparison.OrdinalIgnoreCase)) return IsSettingEnabled(settings, "WF.AREA", true);
            if (string.Equals(category, "Ceiling Finish", StringComparison.OrdinalIgnoreCase)) return IsSettingEnabled(settings, "CF.AREA", true);
            if (string.Equals(category, "Suspended Ceiling", StringComparison.OrdinalIgnoreCase)) return IsSettingEnabled(settings, "SC.AREA", true);
            if (string.Equals(category, "Floor Finish", StringComparison.OrdinalIgnoreCase)) return IsSettingEnabled(settings, "FF.AREA", true);
            if (string.Equals(category, "Waterproof", StringComparison.OrdinalIgnoreCase)) return IsSettingEnabled(settings, "WP.AREA", true);
            if (string.Equals(category, "Kerb", StringComparison.OrdinalIgnoreCase)) return IsAnySettingEnabled(settings, true, "KERB.SIDE", "KERB.TOP");
            if (string.Equals(category, "Others", StringComparison.OrdinalIgnoreCase)) return IsAnySettingEnabled(settings, true, "OTHER.SIDE", "OTHER.BOTTOM");
            return true;
        }

        private static bool IsTasBaseRule(string code, string method)
        {
            if (string.Equals(method, "Classification", StringComparison.OrdinalIgnoreCase)) return true;
            if (code.IndexOf(".QTY.VOLUME", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (code.IndexOf(".QTY.NUMBER", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!code.StartsWith("SOPEN.", StringComparison.OrdinalIgnoreCase) &&
                code.IndexOf(".QTY.AREA", StringComparison.OrdinalIgnoreCase) >= 0 &&
                code.IndexOf(".QTY.OPENING", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return true;
            }

            return false;
        }

        private static bool IsAnySettingEnabled(QsMeasurementSettingsProfile settings, bool fallback, params string[] settingCodes)
        {
            if (settingCodes == null || settingCodes.Length == 0) return fallback;
            return settingCodes.Any(code => IsSettingEnabled(settings, code, fallback));
        }

        private static bool IsSettingEnabled(
            QsMeasurementSettingsProfile settings,
            string settingCode,
            bool fallback,
            params string[] disabledSnippets)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settingCode)) return fallback;

            QsMeasurementSettingRow setting = settings.FindRule(settingCode);
            if (setting == null) return fallback;
            if (!setting.IsEnabled) return false;

            string value = (setting.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            foreach (string snippet in disabledSnippets ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(snippet) &&
                    value.IndexOf(snippet, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            if (string.Equals(value, "No", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "N", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "False", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Exclude", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("0 Not calculate", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("0 No", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Do not calculate", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static string GetFinishSettingPrefix(string ruleCode)
        {
            if (string.IsNullOrWhiteSpace(ruleCode)) return "";
            int dot = ruleCode.IndexOf('.');
            return dot > 0 ? ruleCode.Substring(0, dot) : ruleCode;
        }
    }
}
