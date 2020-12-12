module App

open Elmish
open Feliz
open Fable.Remoting.Client
open IsItKrampus.NET.DataSet.Client.Cropper
open IsItKrampus.NET.DataSet.Shared

let processingApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder (fun typeName methodName -> $"/api/%s{typeName}/%s{methodName}")
    |> Remoting.buildProxy<IProcessingApi>

type Model =
    { Image: ToProcess option
      Crop: Crop
      Zoom: float
      Label: string
      BoundingBox: BoundingBox option
      ApplicationRequestPending: bool }

type Message =
    | SetCrop of Crop
    | SetZoom of float
    | CompleteCrop of croppedArea: Area * croppedAreaPixels: Area
    | LabelChanged of string
    | ImageLoaded of ToProcess
    | RequestProcessingApplication
    | ProcessingApplicationDone of Result
    | Error of exn

let init (): Model * Cmd<Message> =
    let model =
        { Image = None
          Crop = { x = 0; y = 0 }
          Zoom = 1.
          Label = "Other"
          BoundingBox = None
          ApplicationRequestPending = false }

    model, Cmd.OfAsync.either processingApi.getNextImageBase64 () ImageLoaded Error

let update (message: Message) (model: Model): Model * Cmd<Message> =
    match message with
    | SetCrop crop -> { model with Crop = crop }, Cmd.none
    | SetZoom zoom -> { model with Zoom = zoom }, Cmd.none
    | CompleteCrop (_, croppedAreaPixels) ->
        let box =
            { X = croppedAreaPixels.x
              Y = croppedAreaPixels.y
              Width = croppedAreaPixels.width
              Height = croppedAreaPixels.height }

        { model with BoundingBox = Some box } , Cmd.none 
    | LabelChanged label -> { model with Label = label }, Cmd.none
    | ImageLoaded loadedImage -> { model with Image = Some loadedImage }, Cmd.none
    | RequestProcessingApplication ->
        match model.Image, model.BoundingBox with
        | Some toProcess, Some box ->
            let parseLabel (value: string) =
                if value = "Santa" then
                    Santa
                elif value = "Krampus" then
                    Krampus
                else
                    Other

            let processed =
                { FileName = toProcess.FileName
                  Label = parseLabel model.Label
                  BoundingBox = box }

            { model with ApplicationRequestPending = true } , Cmd.OfAsync.either processingApi.applyProcessing processed ProcessingApplicationDone Error
        | _, _ -> model, Cmd.none
    | ProcessingApplicationDone result ->
        match result with
        | Ok ->
            { model with ApplicationRequestPending = false }, Cmd.none
        | Problem reason ->
            printfn $"%s{reason}"
            { model with ApplicationRequestPending = false }, Cmd.none
    | Error error ->
        printfn $"%s{error.Message}"
        model, Cmd.none

let view (model: Model) (dispatch: Message -> unit) =
    match model.Image with
    | Some image ->
        Html.div [ Html.div [ prop.style [ style.custom ("width", "100%")
                                           style.height 299
                                           style.position.relative ]
                              prop.children [ Cropper.cropper [ cropper.onCropChange (SetCrop >> dispatch)
                                                                cropper.onZoomChange (SetZoom >> dispatch)
                                                                cropper.image $"data:image/jpg;base64,%s{image.Base64Content}"
                                                                cropper.onCropComplete
                                                                    (fun a b -> CompleteCrop(a, b) |> dispatch)
                                                                cropper.crop model.Crop
                                                                cropper.zoom model.Zoom ] ] ]
                   Html.div [
                       Html.select [
                           prop.onTextChange (LabelChanged >> dispatch)
                           prop.value model.Label
                           prop.children [
                               Html.option [ prop.value "Other"; prop.text "Other" ]
                               Html.option [ prop.value "Santa"; prop.text "Santa" ]
                               Html.option [ prop.value "Krampus"; prop.text "Krampus" ]
                           ]
                       ]
                   ]
                   Html.div [ Html.button [
                       prop.text "Send selection"
                       prop.onClick (fun _ -> dispatch RequestProcessingApplication)
                       prop.disabled (model.BoundingBox.IsNone || model.ApplicationRequestPending)
                   ] ] ]
    | None -> Html.div "Loading..."

open Elmish.React

Program.mkProgram init update view
|> Program.withReactBatched "app"
|> Program.run
