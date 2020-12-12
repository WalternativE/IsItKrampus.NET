namespace IsItKrampus.NET.DataSet.Client.Cropper

open Feliz
open Fable.Core
open Fable.Core.JsInterop

module Interop =
    [<Emit("Object.assign({}, $0, $1)")>]
    let objectAssign (x: obj) (y: obj) = jsNative

type ICropperProperty =
    inherit IReactProperty

type Crop =
    { x: int; y: int}

type Area =
    { x: float
      y: float
      width: float
      height: float }

[<Erase>]
type cropper =
    static member inline image (source: string) =
        unbox<ICropperProperty> ("image", source)

    static member inline crop (crop: Crop) =
        unbox<ICropperProperty> ("crop", crop)

    static member inline zoom (zoom: float) =
        unbox<ICropperProperty> ("zoom", zoom)

    static member inline aspect (x: ^a, byY: ^b) =
        unbox<ICropperProperty> ("aspect", x / byY)

    static member inline onCropChange (onCropChange: Crop -> unit) =
        unbox<ICropperProperty> ("onCropChange", onCropChange)

    static member inline onZoomChange (onZoomChange: float -> unit) =
        unbox<ICropperProperty> ("onZoomChange", onZoomChange)

    static member inline onCropComplete (onCropComplete: Area -> Area -> unit) =
        let uncurried = System.Func<Area, Area, unit>(onCropComplete)
        unbox<ICropperProperty> ("onCropComplete", uncurried)

[<Erase>]
type Cropper =
    static member inline cropper (properties: ICropperProperty list) =
        let defaults = createObj [
            "image" ==> "https://img.huffingtonpost.com/asset/5ab4d4ac2000007d06eb2c56.jpeg?cache=sih0jwle4e&ops=1910_1000"
            "crop" ==> { x = 0; y = 0 }
            "onCropChange" ==> ignore
        ]
        Interop.reactApi.createElement(importDefault "react-easy-crop", Interop.objectAssign defaults (createObj !!properties))
