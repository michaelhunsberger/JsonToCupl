using System;

namespace JsonToCupl
{
    static class CfgThrowHelper
    {
        public static void PinFileNotFound(string pinFile)
        {
            throw new ConfigException($"File '{pinFile}' does not exist", ErrorCode.PinFileNotFound);
        }

        public static void InputFileNotFound(string inFile)
        {
            throw new ConfigException($"File '{inFile}' does not exist", ErrorCode.InputFileNotFound);
        }

        public static void MissingOutputFile()
        {
            throw new ConfigException("Missing required output file path.", ErrorCode.MissingOutputFile);
        }

        public static void MissingInputFile()
        {
            throw new ConfigException("Missing required input file path.", ErrorCode.MissingInputFile);
        }

        public static void InvalidArgName(string key)
        {
            throw new ConfigException($"Invalid argument '{key}'", ErrorCode.InvalidArgumentName);
        }

        public static void InvalidNumberOfArguments(string extraMsg)
        {
            throw new ConfigException($"Invalid number of arguments. {extraMsg}", ErrorCode.InvalidNumberOfArguments);
        }

        public static void DuplicatePinName(string pinName)
        {
            throw new ConfigException($"Duplicate pin name '{pinName}' detected.", ErrorCode.DuplicatePinName);
        }

        public static void InvalidPin(string sPinNum)
        {
            throw new ConfigException($"Cannot parse pin number '{sPinNum}'", ErrorCode.PinNumberParseError);
        }

        public static void AmbiguousOrModuleNotFound()
        {
            throw new ConfigException("Ambiguous number of modules or module not found", ErrorCode.AmbiguousOrModuleNotFound);
        }
    }
}