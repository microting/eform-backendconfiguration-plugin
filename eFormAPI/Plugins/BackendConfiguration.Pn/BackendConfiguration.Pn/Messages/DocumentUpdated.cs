namespace BackendConfiguration.Pn.Messages;

public class DocumentUpdated
{
    public int DocumentId { get; set; }

    public DocumentUpdated(int documentId)
    {
        DocumentId = documentId;
    }
}