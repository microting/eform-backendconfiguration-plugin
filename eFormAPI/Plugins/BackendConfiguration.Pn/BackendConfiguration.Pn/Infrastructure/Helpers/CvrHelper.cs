#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public class CvrHelper
{
    public async Task<Result> GetCompanyInfo(int number)
    {
        if (number == 0)
        {
            return new Result
            {
                Industrycode = "0"
            };
        }
        if (number == 1)
        {
            return new Result
            {
                Industrycode = "1"
            };
        }
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-agent", "Microting eForm - CVR opslag");
        var url = $"http://cvrapi.dk/api?vat={number}&country=dk";
        Console.WriteLine($"calling url is: {url}");
        var response = await client.GetAsync(url).ConfigureAwait(false);
        var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.WriteLine($"result is: {result}");

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        options.Converters.Add(new StringConverter());

        var res = JsonSerializer.Deserialize<Result>(result, options);

        try
        {
            if (res?.Industrycode?.Length == 5)
            {
                res.Industrycode = "0" + res.Industrycode;
            }
        } catch (Exception)
        {
            if (res != null)
            {
                res.Industrycode = "0";
            }
        }

        return res ?? new Result { Industrycode = "0" };
    }
}

public class Owner
{
    public string Name { get; set; } = null!;
}

public class PoductionUnit
{
    public int Pno { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public string Zipcode { get; set; } = null!;
    public string City { get; set; } = null!;
    public bool @protected { get; set; }
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string? Fax { get; set; }
    public string Startdate { get; set; } = null!;
    public string? Enddate { get; set; }
    public string Employees { get; set; } = null!;
    public string? Addressco { get; set; }
    public int Industrycode { get; set; }
    public string Industrydesc { get; set; } = null!;
    public int Companycode { get; set; }
    public string? Companydesc { get; set; }
    public string? Creditstartdate { get; set; }
    public int? Creditstatus { get; set; }
    public bool? Creditbankrupt { get; set; }
}

public class Result
{
    public int Vat { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public string Zipcode { get; set; } = null!;
    public string City { get; set; } = null!;
    // public bool @protected { get; set; }
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public string?  Fax { get; set; }
    public string Startdate { get; set; } = null!;
    public string? Enddate { get; set; }
    public int? Employees { get; set; }
    public string Addressco { get; set; } = null!;
    public string? Industrycode { get; set; }
    public string Industrydesc { get; set; } = null!;
    public int Companycode { get; set; }
    public string Companydesc { get; set; } = null!;
    public string? Creditstartdate { get; set; }
    public int? Creditstatus { get; set; }
    public bool? Creditbankrupt { get; set; }
    public ICollection<Owner> Owners { get; set; } = [];
    public ICollection<PoductionUnit> Productionunits { get; set; } = [];
    public int T { get; set; }
    public int Version { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
}

public class StringConverter : System.Text.Json.Serialization.JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {

        if (reader.TokenType == JsonTokenType.Number)
        {
            var stringValue = reader.GetInt32();
            return stringValue.ToString();
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? string.Empty;
        }

        throw new System.Text.Json.JsonException();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

}