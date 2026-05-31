using System;
using Autodesk.Revit.DB;

namespace CamboBIM.Revit2024.Addin
{
    internal static class RevitTransactionRunner
    {
        public static OperationResult Run(Document document, string transactionName, Func<OperationResult> action)
        {
            if (document == null)
            {
                return OperationResult.Failure("No active Revit document.", "REVIT_DOCUMENT_MISSING");
            }

            if (action == null)
            {
                return OperationResult.Failure("No Revit action was provided.", "REVIT_ACTION_MISSING");
            }

            string safeName = string.IsNullOrWhiteSpace(transactionName)
                ? "MHNK Operation"
                : transactionName.Trim();

            using (var transaction = new Transaction(document, safeName))
            {
                try
                {
                    TransactionStatus startStatus = transaction.Start();
                    if (startStatus != TransactionStatus.Started)
                    {
                        return OperationResult.Failure(
                            "Could not start Revit transaction: " + startStatus,
                            "REVIT_TRANSACTION_START_FAILED");
                    }

                    OperationResult result = action() ?? OperationResult.Success();
                    if (!result.Succeeded)
                    {
                        transaction.RollBack();
                        return result;
                    }

                    TransactionStatus commitStatus = transaction.Commit();
                    if (commitStatus != TransactionStatus.Committed)
                    {
                        return OperationResult.Failure(
                            "Could not commit Revit transaction: " + commitStatus,
                            "REVIT_TRANSACTION_COMMIT_FAILED");
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    TryRollback(transaction);
                    MhnkLogger.Error("Revit transaction failed: " + safeName, ex);
                    return OperationResult.Failure(
                        "Revit transaction failed: " + ex.Message,
                        "REVIT_TRANSACTION_EXCEPTION",
                        ex);
                }
            }
        }

        private static void TryRollback(Transaction transaction)
        {
            try
            {
                if (transaction != null && transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }
            }
            catch
            {
            }
        }
    }
}
