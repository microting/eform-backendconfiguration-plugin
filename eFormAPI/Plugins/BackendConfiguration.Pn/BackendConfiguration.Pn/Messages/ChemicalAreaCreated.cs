namespace BackendConfiguration.Pn.Messages;

public class ChemicalAreaCreated
{
    public int PropertyId { get; protected set; }

    public ChemicalAreaCreated(int propertyId)
    {
        PropertyId = propertyId;
    }
}