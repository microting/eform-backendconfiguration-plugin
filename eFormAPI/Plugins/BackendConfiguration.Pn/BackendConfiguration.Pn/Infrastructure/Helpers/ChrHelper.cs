using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public class ChrHelper
{
    public async Task<ChrResult> GetCompanyInfo(int number)
    {
        // var client = new HttpClient();
        // client.DefaultRequestHeaders.Add("User-agent", "Microting eForm - CVR opslag");
        // var url = $"https://chrregister.microting.com/Chr?chrNumber={number}";
        // Console.WriteLine($"calling url is: {url}");
        // var response = await client.GetAsync(url).ConfigureAwait(false);
        // var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        // Console.WriteLine($"result is: {result}");
        // JsonSerializerOptions options = new JsonSerializerOptions
        // {
        //     PropertyNameCaseInsensitive = true,
        //     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        //     NumberHandling = JsonNumberHandling.AllowReadingFromString
        // };
        // options.Converters.Add(new StringConverter());
        //
        // var res = JsonSerializer.Deserialize<ChrResult>(result, options);
        //
        // return res;

        // Temporary return for testing without calling the real service
        // TODO - remove when real service is working
        return new ChrResult
        {
            ChrNummer = number.ToString(),
            Ejendom = new Ejendom
            {
                adresse = "",
                byNavn = "",
                postNummer = "",
                postDistrikt = "",
                kommuneNummer = "",
                kommuneNavn = "",
                datoOpret = DateTime.Now.AddYears(-1).ToString("yyyy-MM-dd"),
                datoOpdatering = DateTime.Now.ToString("yyyy-MM-dd")
            }
        };
    }
}

public class ChrResult
{
    public string ChrNummer { get; set; }
    public Ejendom Ejendom { get; set; }
}

public class Ejendom
{
    public string adresse { get; set; }
    public string byNavn { get; set; }
    public string postNummer { get; set; }
    public string postDistrikt { get; set; }
    public string kommuneNummer { get; set; }
    public string kommuneNavn { get; set; }
    public string datoOpret { get; set; }
    public string datoOpdatering { get; set; }
}