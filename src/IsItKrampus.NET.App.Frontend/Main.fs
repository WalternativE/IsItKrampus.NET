module IsItKrampus.NET.App.Frontend.Client.Main

open System
open System.IO
open System.Net.Http
open System.Text.Json
open Elmish
open FSharp.Control.Tasks.V2
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

type Label =
    | Krampus
    | Santa
    | Other
    | InferenceError

type FormActivity =
    | PendingRequest
    | Interactive

type Model =
    { Url: string
      ImageDataString: string option
      FormActivity: FormActivity
      LastInferenceResult: Label option }

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
      ImageDataString = None
      FormActivity = Interactive
      LastInferenceResult = None },
    Cmd.none

let update (logger: ILogger<_>) (httpClient: HttpClient) message model =
    let loadFileFromUrl useCorsProxy =
        task {
            let corsProxyUrl = "https://cors-anywhere.herokuapp.com"

            let url =
                if useCorsProxy then $"{corsProxyUrl}/{model.Url}" else model.Url

            let! resp = httpClient.GetAsync(url)

            logger.LogInformation("Web request finished.")

            let! bytes = resp.Content.ReadAsByteArrayAsync()

            logger.LogInformation("Got stream from the request.")

            let mutable format: IImageFormat = null
            use img = Image.Load(bytes, &format)

            logger.LogInformation($"Loaded image")

            use resized =
                img.Clone(fun i -> i.Resize(299, 299) |> ignore)

            use memStream = new MemoryStream()
            do! resized.SaveAsJpegAsync(memStream)

            let base64 =
                memStream.ToArray() |> Convert.ToBase64String

            return base64
        }

    match message with
    | OnUrlInput n -> { model with Url = n }, Cmd.none

    | LoadFile f ->
        let loadDataString (f: IBrowserFile) =
            task {
                use reader =
                    new StreamReader(f.OpenReadStream(maxAllowedFileSize))

                use memoryStream = new MemoryStream()

                do! reader.BaseStream.CopyToAsync(memoryStream)

                let mutable format: IImageFormat = null

                use img =
                    Image.Load(memoryStream.ToArray(), &format)

                logger.LogInformation($"Loaded image")

                use resized =
                    img.Clone(fun i -> i.Resize(299, 299) |> ignore)

                use memStream = new MemoryStream()
                do! resized.SaveAsJpegAsync(memStream)

                let base64 =
                    memStream.ToArray() |> Convert.ToBase64String

                logger.LogInformation("Loaded image.")

                return base64
            }

        { model with
              FormActivity = PendingRequest
              LastInferenceResult = None },
        Cmd.OfTask.either loadDataString f FinishFileDataLoad Error

    | LoadFileFromUrl ->
        { model with
              FormActivity = PendingRequest
              LastInferenceResult = None },
        Cmd.OfTask.either loadFileFromUrl false FinishFileDataLoad Error

    | RepeatUsingCorsProxy -> model, Cmd.OfTask.either loadFileFromUrl true FinishFileDataLoad ErrorWithoutCorsAttempt

    | RequestInference ->
        let sendToLambda data =
            task {
                let stringContent =
                    new StringContent(JsonSerializer.Serialize(data))

                let! resp =
                    httpClient.PostAsync(
                        "http://localhost:3000/2015-03-31/functions/function/invocations",
                        stringContent
                    )

                let! respContent = resp.Content.ReadAsStringAsync()

                let label: string = JsonSerializer.Deserialize(respContent)

                return label
            }

        match model.ImageDataString with
        | Some data -> model, Cmd.OfTask.either sendToLambda data ReceivedInferenceResult ErrorWithoutCorsAttempt
        | None -> model, Cmd.none

    | ReceivedInferenceResult label ->
        logger.LogInformation($"And we got a: {label}")

        let label =
            match label.ToLower() with
            | "santa" -> Santa
            | "krampus" -> Krampus
            | "other" -> Other
            | _ -> InferenceError

        { model with
              LastInferenceResult = Some label },
        Cmd.none

    | FinishFileDataLoad data ->
        { model with
              ImageDataString = Some data
              FormActivity = Interactive },
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

        let activity =
            if couldBeCors then PendingRequest else Interactive

        { model with FormActivity = activity }, (if couldBeCors then Cmd.ofMsg RepeatUsingCorsProxy else Cmd.none)

    | Error e ->
        logger.LogError($"Error Type {e.GetType()}")
        logger.LogError(e.Message)

        let couldBeCors =
            e.Message.ToLower().Contains("failed to fetch")

        let activity =
            if couldBeCors then PendingRequest else Interactive

        { model with FormActivity = activity }, (if couldBeCors then Cmd.ofMsg RepeatUsingCorsProxy else Cmd.none)

    | ErrorWithoutCorsAttempt e ->
        logger.LogError("Failed. Won't try to reattempt with CORS proxy. Better to give up now.")
        logger.LogError(e.Message)

        { model with
              FormActivity = Interactive },
        Cmd.none

    | LogThing o ->
        logger.LogInformation(sprintf "%A" o)
        model, Cmd.none

let inputFile (attrs: Attr list) = comp<InputFile> attrs []

let interactiveForm model dispatch =
    div [] [
        div [ attr.``class`` "field" ] [
            label [ attr.``class`` "label" ] [
                text "Image URL"
            ]
            div [ attr.``class`` "control" ] [
                input [ attr.``type`` "text"
                        attr.value model.Url
                        on.input (fun e -> OnUrlInput(unbox e.Value) |> dispatch) ]
            ]
        ]

        div [ attr.``class`` "field" ] [
            div [ attr.``class`` "control" ] [
                button [ attr.``class`` "button"
                         on.click (fun _ -> dispatch LoadFileFromUrl) ] [
                    text "Load image URL"
                ]
            ]
        ]

        hr []

        div [ attr.``class`` "file" ] [
            label [ attr.``class`` "file-label" ] [
                inputFile [ attr.``class`` "file-input"
                            attr.accept "image/jpgeg"
                            attr.callback "OnChange" (fun (e: InputFileChangeEventArgs) -> LoadFile e.File |> dispatch) ]
                span [ attr.``class`` "file-cta" ] [
                    span [ attr.``class`` "file-label" ] [
                        text "Upload Image File"
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) dispatch =
    section [ attr.``class`` "section" ] [
        div [ attr.``class`` "container" ] [
            div [ attr.``class`` "notification" ] [
                text "You can load an image via an URL or via uploading an image."
                text "Please be advised, that images are rescaled in the the browser."
                text
                    "Due to currently existing limitations in .NET WebAssembly this can take a while if you try to access big images."
            ]

            hr []

            interactiveForm model dispatch

            match model.FormActivity with
            | Interactive -> empty
            | PendingRequest ->
                div [ attr.``class`` "field"
                      attr.style "margin-top:8px" ] [
                    div [ attr.``class`` "control" ] [
                        progress [ attr.``class`` "progress is-primary"
                                   attr.max "100" ] []
                    ]
                ]

            match model.LastInferenceResult with
            | None -> empty
            | Some label ->
                hr []

                div [ attr.``class`` "block" ] [
                    span [] [
                        text "The last inference result was"
                        strong [] [ text $": {label}" ]
                    ]
                ]

            match model.ImageDataString with
            | None -> empty
            | Some data ->
                hr []

                figure [ attr.``class`` "image"
                         attr.style "max-width:420px" ] [
                    img [ attr.src $"data:image/jpeg;base64,{data}" ]
                ]

                div [ attr.``class`` "field" ] [
                    div [ attr.``class`` "control"
                          attr.style "margin-top:8px" ] [
                        button [ attr.``class`` "button"
                                 on.click (fun _ -> dispatch RequestInference) ] [
                            text "Request Inference"
                        ]
                    ]
                ]
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
