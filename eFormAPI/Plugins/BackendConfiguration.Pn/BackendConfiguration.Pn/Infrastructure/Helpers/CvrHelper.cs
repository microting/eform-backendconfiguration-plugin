using System;
using System.Collections.Generic;
using System.Net;
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
            return new Result()
            {
                Industrycode = "0",
            };
        }
        if (number == 1)
        {
            return new Result()
            {
                Industrycode = "1",
            };
        }
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-agent", "Microting eForm - CVR opslag");
        var url = $"http://cvrapi.dk/api?vat={number}&country=dk";
        Console.WriteLine($"calling url = {url}");
        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();

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
            if (res.Industrycode.Length == 5)
            {
                res.Industrycode = "0" + res.Industrycode;
            }
        } catch (Exception)
        {
            res.Industrycode = "0";
        }

        return res;
    }
}

public class Owner
{
    public string Name { get; set; }
}

public class PoductionUnit
{
    public int Pno { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public string Zipcode { get; set; }
    public string City { get; set; }
    public bool @protected { get; set; }
    public string Phone { get; set; }
    [CanBeNull] public string Email { get; set; }
    [CanBeNull] public string Fax { get; set; }
    public string Startdate { get; set; }
    [CanBeNull] public string Enddate { get; set; }
    public int Employees { get; set; }
    [CanBeNull] public string Addressco { get; set; }
    public int Industrycode { get; set; }
    public string Industrydesc { get; set; }
    public int Companycode { get; set; }
    [CanBeNull] public string Companydesc { get; set; }
    [CanBeNull] public string Creditstartdate { get; set; }
    public int? Creditstatus { get; set; }
    public bool? Creditbankrupt { get; set; }
}

public class Result
{
    public int Vat { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public string Zipcode { get; set; }
    public string City { get; set; }
    // public bool @protected { get; set; }
    public string Phone { get; set; }
    [CanBeNull] public string Email { get; set; }
    [CanBeNull] public string  Fax { get; set; }
    public string Startdate { get; set; }
    [CanBeNull] public string Enddate { get; set; }
    public int? Employees { get; set; }
    public string Addressco { get; set; }
    [CanBeNull] public string Industrycode { get; set; }
    public string Industrydesc { get; set; }
    public int Companycode { get; set; }
    public string Companydesc { get; set; }
    [CanBeNull] public string Creditstartdate { get; set; }
    public int? Creditstatus { get; set; }
    public bool? Creditbankrupt { get; set; }
    public ICollection<Owner> Owners { get; set; }
    public ICollection<PoductionUnit> Productionunits { get; set; }
    public int T { get; set; }
    public int Version { get; set; }
    [CanBeNull] public string Error { get; set; }
    [CanBeNull] public string Message { get; set; }
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
            return reader.GetString();
        }

        throw new System.Text.Json.JsonException();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

}