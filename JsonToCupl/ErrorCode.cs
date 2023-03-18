namespace JsonToCupl
{
    enum ErrorCode
    {
        MissingArguments = 1,
        InvalidArgumentName,
        MissingInputFile,
        MissingOutputFile,
        InputFileNotFound,
        PinFileNotFound,
        InvalidNumberOfArguments,
        DuplicatePinName,
        CodeGenerationError,
        InvalidJsonFile,
        PinNumberParseError,
        AmbiguousOrModuleNotFound
    }
}