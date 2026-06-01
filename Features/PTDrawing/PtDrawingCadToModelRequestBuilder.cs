using System;
using System.Collections.Generic;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingCadToModelRequestBuilder
    {
        public static OperationResult FillCadImport(
            CadToModelRequest request,
            string sourcePath,
            AdaptCadImportMode importMode,
            string markPrefix,
            int startNumber,
            int digits,
            AdaptPtShopMarkSequenceMode sequenceMode,
            bool preserveCadShopMarks)
        {
            if (request == null)
            {
                return OperationResult.Failure("CAD2MODEL request is not available.", "PT_REQUEST_MISSING");
            }

            OperationResult<PtDrawingValidatedSource> sourceValidation = PtDrawingImportWorkflowService.ValidateCadSource(sourcePath);
            if (!sourceValidation.Succeeded)
            {
                return OperationResult.Failure(sourceValidation.Message, sourceValidation.ErrorCode, sourceValidation.Exception);
            }

            ApplyCommonMarkSettings(request, markPrefix, startNumber, digits, sequenceMode, preserveCadShopMarks);
            request.AdaptCadSourcePath = sourceValidation.Value.SourcePath;
            request.AdaptCadImportMode = importMode;
            request.AdaptTendonSourcePath = "";
            request.AdaptTendonProfileSegments = new List<AdaptTendonProfileSegmentPayload>();
            request.RequestType = CadToModelRequestType.ImportAdaptCadDrawing;

            return OperationResult.Success("Prepared ADAPT CAD request: " + sourceValidation.Value.SourcePath);
        }

        public static OperationResult FillDirectProfileImport(
            CadToModelRequest request,
            string sourcePath,
            AdaptTendonImportReadResult readResult,
            string markPrefix,
            int startNumber,
            int digits,
            AdaptPtShopMarkSequenceMode sequenceMode,
            bool preserveCadShopMarks)
        {
            if (request == null)
            {
                return OperationResult.Failure("CAD2MODEL request is not available.", "PT_REQUEST_MISSING");
            }

            if (readResult == null || readResult.Segments == null || readResult.Segments.Count == 0)
            {
                return OperationResult.Failure("No tendon/profile segments were found.", "PT_SEGMENTS_EMPTY");
            }

            OperationResult<PtDrawingValidatedSource> sourceValidation = PtDrawingImportWorkflowService.ValidateDirectProfileSource(sourcePath);
            if (!sourceValidation.Succeeded)
            {
                return OperationResult.Failure(sourceValidation.Message, sourceValidation.ErrorCode, sourceValidation.Exception);
            }

            ApplyCommonMarkSettings(request, markPrefix, startNumber, digits, sequenceMode, preserveCadShopMarks);
            request.AdaptTendonSourcePath = sourceValidation.Value.SourcePath;
            request.AdaptTendonImportMode = readResult.Mode;
            request.AdaptTendonProfileSegments = readResult.Segments;
            request.AdaptCadSourcePath = "";
            request.RequestType = CadToModelRequestType.ImportAdaptTendonProfiles;

            return OperationResult.Success("Prepared ADAPT direct import request: " + sourceValidation.Value.SourcePath);
        }

        private static void ApplyCommonMarkSettings(
            CadToModelRequest request,
            string markPrefix,
            int startNumber,
            int digits,
            AdaptPtShopMarkSequenceMode sequenceMode,
            bool preserveCadShopMarks)
        {
            request.AdaptShopMarkPrefix = ExtensionTextUtility.NormalizeAdaptShopMarkPrefix(markPrefix, "PT");
            request.AdaptShopMarkStartNumber = Math.Max(1, startNumber);
            request.AdaptShopMarkDigits = Math.Max(1, Math.Min(6, digits));
            request.AdaptShopMarkSequenceMode = sequenceMode;
            request.AdaptPreserveCadShopMarks = preserveCadShopMarks;
        }
    }
}
