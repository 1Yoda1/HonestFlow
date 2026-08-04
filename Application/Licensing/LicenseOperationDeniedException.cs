using System;
using HonestFlow.Models.Licensing;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseOperationDeniedException : InvalidOperationException
    {
        public LicenseOperationDeniedException(
            LicenseOperation operation,
            LicenseAccessResult access)
            : base(access?.Message ?? "Операция запрещена текущей лицензией.")
        {
            Operation = operation;
            TechnicalCode = access?.TechnicalCode;
        }

        public LicenseOperation Operation { get; }
        public string TechnicalCode { get; }
    }
}
