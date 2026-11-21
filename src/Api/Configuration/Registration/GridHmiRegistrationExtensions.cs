using Hmi.Services.Grid;

namespace Api.Configuration.Registration;

public static class GridHmiRegistrationExtensions
{
    public static WebApplicationBuilder AddGridHmiServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<GridEquipmentCatalog>();
        return builder;
    }
}
