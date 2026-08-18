namespace ContosoDashboard.Services;

public class UnauthorizedDocumentAccessException : Exception
{
    public UnauthorizedDocumentAccessException(string message) : base(message)
    {
    }
}

public class DuplicateDocumentException : Exception
{
    public DuplicateDocumentException(string message) : base(message)
    {
    }
}

public class DocumentConcurrencyException : Exception
{
    public DocumentConcurrencyException(string message) : base(message)
    {
    }
}
