open System
open System.IO
open System.Net.Http
open System.Text.Json

let httpClient = new HttpClient()

let testImage = Path.Combine(__SOURCE_DIRECTORY__, @"..\data\prepared\d740daf3-add4-4c6f-8634-00f4b2a4e9e1.jpg")
let base64 =
    File.ReadAllBytes testImage
    |> Convert.ToBase64String

let stringContent = new StringContent(JsonSerializer.Serialize(base64))

let proxiedUrl =  "http://localhost:3000/2015-03-31/functions/function/invocations"
let resp =
    // httpClient.PostAsync("http://localhost:9000/2015-03-31/functions/function/invocations", stringContent)
    httpClient.PostAsync(proxiedUrl, stringContent)
    |> Async.AwaitTask
    |> Async.RunSynchronously

resp.IsSuccessStatusCode

let respContent =
    resp.Content.ReadAsStringAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously

let label: string = JsonSerializer.Deserialize(respContent)
printfn "%s" label
