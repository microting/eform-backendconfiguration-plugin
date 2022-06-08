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
                Industrycode = 0,
            };
        }
        if (number == 1)
        {
            return new Result()
            {
                Industrycode = 1,
            };
        }
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-agent", "Microting eForm - CVR opslag");
        var response = await client.GetAsync(string.Format("http://cvrapi.dk/api?vat={0}&country=dk", number.ToString()));
        var result = await response.Content.ReadAsStringAsync();

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        };
        var res = JsonSerializer.Deserialize<Result>(result, options);

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
    public int Industrycode { get; set; }
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