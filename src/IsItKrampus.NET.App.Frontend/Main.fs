module IsItKrampus.NET.App.Frontend.Client.Main

open System
open System.IO
open System.Net.Http
open System.Text.Json
open Elmish
open Bolero
open Bolero.Html
open Bolero.Templating.Client
open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Forms
open Microsoft.Extensions.Logging
open SixLabors.ImageSharp
open SixLabors.ImageSharp.Processing
open SixLabors.ImageSharp.Formats

// 15 MB should be enough
let maxAllowedFileSize = int64 (1024 * 1024 * 15)

type Model =
    { Url: string
      ImageDataString: string option }

type Message =
    | OnUrlInput of string
    | LogThing of obj
    | LoadFile of IBrowserFile
    | LoadFileFromUrl
    | RepeatUsingCorsProxy
    | RequestInference
    | ReceivedInferenceResult of string
    | FinishFileDataLoad of string
    | Error of exn
    | ErrorWithoutCorsAttempt of exn

let initModel =
    { Url = String.Empty
      ImageDataString = None },
    Cmd.none

let update (logger: ILogger<_>) (httpClient: HttpClient) message model =
    let loadFileFromUrl useCorsProxy =
        async {
            let corsProxyUrl = "https://cors-anywhere.herokuapp.com"

            let url =
                if useCorsProxy then $"{corsProxyUrl}/{model.Url}" else model.Url

            let! resp = httpClient.GetAsync(url) |> Async.AwaitTask

            let! bytes =
                resp.Content.ReadAsByteArrayAsync()
                |> Async.AwaitTask

            let mutable format: IImageFormat = null
            use img = Image.Load(bytes, &format)

            use resized =
                img.Clone(fun i -> i.Resize(299, 299) |> ignore)

            logger.LogInformation($"Loaded image of format {format.Name}")

            use memStream = new MemoryStream()

            do!
                resized.SaveAsJpegAsync(memStream)
                |> Async.AwaitTask

            let base64 =
                memStream.ToArray() |> Convert.ToBase64String

            return base64
        }

    match message with
    | OnUrlInput n -> { model with Url = n }, Cmd.none

    | LoadFile f ->
        let loadDataString (f: IBrowserFile) =
            async {
                use reader =
                    new StreamReader(f.OpenReadStream(maxAllowedFileSize))

                use memoryStream = new MemoryStream()

                do!
                    reader.BaseStream.CopyToAsync(memoryStream)
                    |> Async.AwaitTask

                let base64 =
                    Convert.ToBase64String(memoryStream.ToArray())

                logger.LogInformation("Loaded image.")
                logger.LogInformation(base64)

                return base64
            }

        model, Cmd.OfAsync.either loadDataString f FinishFileDataLoad Error

    | LoadFileFromUrl -> model, Cmd.OfAsync.either loadFileFromUrl false FinishFileDataLoad Error

    | RepeatUsingCorsProxy -> model, Cmd.OfAsync.either loadFileFromUrl true FinishFileDataLoad ErrorWithoutCorsAttempt

    | RequestInference ->
        let sendToLambda data =
            async {
                let stringContent =
                    new StringContent(JsonSerializer.Serialize(data))

                let! resp =
                    httpClient.PostAsync(
                        "http://localhost:3000/2015-03-31/functions/function/invocations",
                        stringContent
                    )
                    |> Async.AwaitTask

                let! respContent =
                    resp.Content.ReadAsStringAsync()
                    |> Async.AwaitTask

                let label: string = JsonSerializer.Deserialize(respContent)

                return label
            }

        match model.ImageDataString with
        | Some data ->
            model, Cmd.OfAsync.either sendToLambda data ReceivedInferenceResult ErrorWithoutCorsAttempt
        | None ->
            model, Cmd.none

    | ReceivedInferenceResult label ->
        logger.LogInformation($"And we got a: {label}")
        model, Cmd.none

    | FinishFileDataLoad data ->
        { model with
              ImageDataString = Some data },
        Cmd.none

    | Error e when e.GetType() = typeof<AggregateException> ->
        let e = e :?> AggregateException

        let couldBeCors =
            e.InnerExceptions
            |> Seq.fold
                (fun prev curr ->
                    (curr.Message.ToLower().Contains("failed to fetch"))
                    || prev)
                false

        model, (if couldBeCors then Cmd.ofMsg RepeatUsingCorsProxy else Cmd.none)
    | Error e ->
        logger.LogError($"Error Type {e.GetType()}")
        logger.LogError(e.Message)

        let couldBeCors =
            e.Message.ToLower().Contains("failed to fetch")

        model, (if couldBeCors then Cmd.ofMsg RepeatUsingCorsProxy else Cmd.none)
    | ErrorWithoutCorsAttempt e ->
        logger.LogError("Failed. Won't try to reattempt with CORS proxy. Better to give up now.")
        logger.LogError(e.Message)

        model, Cmd.none
    | LogThing o ->
        logger.LogInformation(sprintf "%A" o)
        model, Cmd.none

let inputFile (attrs: Attr list) = comp<InputFile> attrs []

let view (model: Model) dispatch =
    div [] [
        input [ attr.``type`` "text"
                attr.value model.Url
                on.input (fun e -> OnUrlInput(unbox e.Value) |> dispatch) ]
        button [ on.click (fun _ -> dispatch LoadFileFromUrl) ] [
            text "Load image url"
        ]
        br []
        inputFile [ attr.accept "image/jpgeg"
                    attr.callback "OnChange" (fun (e: InputFileChangeEventArgs) -> LoadFile e.File |> dispatch) ]
        br []
        match model.ImageDataString with
        | None -> empty
        | Some data ->
            img [ attr.src $"data:image/jpeg;base64,{data}" ]

            button [ on.click (fun _ -> dispatch RequestInference) ] [
                text "Request Inference"
            ]
    ]

type MyApp() =
    inherit ProgramComponent<Model, Message>()
    let httpClient = new HttpClient()

    [<Inject>]
    member val Logger = Unchecked.defaultof<ILogger<MyApp>> with get, set

    override this.Program =
        Program.mkProgram (fun _ -> initModel) (update this.Logger httpClient) view
#if DEBUG
        |> Program.withHotReload
#endif
