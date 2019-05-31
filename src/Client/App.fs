module App

open Elmish

open Fable
open Fable.FontAwesome
open Fable.Core.JsInterop
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Fable.PowerPack
open Fable.Recharts
open Fable.Recharts.Props

open Fulma

open Shared
open Helpers
open Thoth.Json

/// The different elements of the completed report.
type Report =
    { Location : LocationResponse
      Crimes : CrimeResponse array
      Weather: WeatherResponse }

let emptyReport = 
    { Location = { DistanceToLondon = 0.; Postcode = ""; Location = { Town = ""; Region = "" ; LatLong = { Latitude = 0.; Longitude = 0. } } }
      Crimes = [||]
      Weather = { AverageTemperature = 0.; WeatherType = WeatherType.Clear }   
    }  

type ServerState = Idle | Loading | ServerError of string

/// The overall data model driving the view.
type Model =
    { Postcode : string
      ValidationError : string option
      ServerState : ServerState
      Report : Report option }

/// The different types of messages in the system.
type Msg =
    | GetReport
    | Clear
    | PostcodeChanged of string
    | GotReport of Report
    | ErrorMsg of exn

/// The init function is called to start the message pump with an initial view.
let init () =
    { Postcode = null
      Report = None
      ValidationError = None
      ServerState = Idle }, Cmd.ofMsg (PostcodeChanged "")

let inline getJson<'T> (response: Fetch.Fetch_types.Response) =
    response.json<'T>()

let inline getSomeJson<'T> (response: Option<Fetch.Fetch_types.Response>) =
    match response with
    | Some a -> a.json<'T>() |> Promise.map Some
    | None -> Promise.lift None

let inline extract<'b> a =
    match a with
    | Ok e -> getJson<'b[]> e
    | Error _ -> Promise.lift [||]

let inline toOpt (a:Result<Fetch.Fetch_types.Response, System.Exception>) =
    match a with
    | Ok e -> Some e
    | Error _ -> None 

type WorkResult =
    | Location of LocationResponse
    | Crime of Option<CrimeResponse[]>
    | Weather of WeatherResponse

let stateFolder (state:Report) current =
    match current with
    | Location l -> { state with Location = l }
    | Crime c -> 
        match c with
        | Some cs -> { state with Crimes = cs }
        | None -> state            
    | Weather w -> { state with Weather = w }

let getResponse postcode = promise {

    let location = 
        Fetch.postRecord "/api/distance/" postcode [] 
        |> Promise.bind (getJson<LocationResponse>) 
        |> Promise.map Location
    let weather = 
        Fetch.postRecord "/api/getWeather/" postcode [] 
        |> Promise.bind (getJson<WeatherResponse>)
        |> Promise.map Weather
    let crime = 
        Fetch.tryPostRecord "api/crime/" postcode [] 
        |> Promise.bind (toOpt >> getSomeJson<CrimeResponse[]> )
        |> Promise.map Crime

    let! results = Promise.Parallel [ location; weather; crime ]

    return Array.fold stateFolder emptyReport results }

/// The update function knows how to update the model given a message.
let update msg model =
    match model, msg with
    | { ValidationError = None; Postcode = postcode }, GetReport ->
        { model with ServerState = Loading }, Cmd.ofPromise getResponse postcode GotReport ErrorMsg
    | _, GetReport -> model, Cmd.none
    | _, GotReport response ->
        { model with
            ValidationError = None
            Report = Some response
            ServerState = Idle }, Cmd.none
    | _, PostcodeChanged p ->
        let validation = 
            match Validation.isValidPostcode p with
            | false -> Some (sprintf "%s" p)
            | true -> None
        { model with 
                Postcode = p
                ValidationError = validation}, Cmd.none
    | _, ErrorMsg e -> { model with ServerState = ServerError e.Message }, Cmd.none
    | _, Clear -> 
        { model with 
            Postcode = ""
            ValidationError = Some ""
            Report = None
            ServerState = ServerState.Idle }, Cmd.none

[<AutoOpen>]
module ViewParts =
    let basicTile title options content =
        Tile.tile options [
            Notification.notification [ Notification.Props [ Style [ Height "100%"; Width "100%" ] ] ]
                (Heading.h2 [] [ str title ] :: content)
        ]
    let childTile title content =
        Tile.child [ ] [
            Notification.notification [ Notification.Props [ Style [ Height "100%"; Width "100%" ] ] ]
                (Heading.h2 [ ] [ str title ] :: content)
        ]

    let crimeTile crimes =
        let cleanData = crimes |> Array.map (fun c -> { c with Crime = c.Crime.[0..0].ToUpper() + c.Crime.[1..].Replace('-', ' ') } )
        basicTile "Crime" [ ] [
            barChart
                [ Chart.Data cleanData
                  Chart.Width 600.
                  Chart.Height 500.
                  Chart.Layout Vertical ]
                [ xaxis [ Cartesian.Type "number" ] []
                  yaxis [ Cartesian.Type "category"; Cartesian.DataKey "Crime"; Cartesian.Width 200. ] []
                  bar [ Cartesian.DataKey "Incidents" ] [] ]
        ]

    let getBingMapUrl latLong =
        sprintf "https://www.bing.com/maps/embed?h=400&w=800&cp=%f~%f&lvl=11&typ=s&FORM=MBEDV8" latLong.Latitude latLong.Longitude

    let bingMapTile (latLong:LatLong) =
        let url = getBingMapUrl 
        basicTile "Map" [ Tile.Size Tile.Is12 ] [
            iframe [
                Style [ Height 410; Width 810 ]
                Src (getBingMapUrl latLong)
            ] [ ]
        ]

    let weatherTile weatherReport =
        childTile "Weather" [
            Level.level [ ] [
                Level.item [ Level.Item.HasTextCentered ] [
                    div [ ] [
                        Level.heading [ ] [
                            Image.image [ Image.Is128x128 ] [
                                img [ Src(sprintf "https://www.metaweather.com/static/img/weather/%s.svg" weatherReport.WeatherType.Abbreviation) ]
                            ]
                        ]
                        Level.title [ ] [
                            Heading.h3 [ Heading.Is4; Heading.Props [ Style [ Width "100%" ] ] ] [
                                str (weatherReport.WeatherType |> string)
                                br []
                                str (sprintf "%s °C" (string (weatherReport.AverageTemperature |> roundF 1 )))
                            ]
                        ]
                    ]
                ]
            ]
        ]
    let locationTile model =
        childTile "Location" [
            div [ ] [
                Heading.h3 [ ] [ str model.Location.Location.Town ]
                Heading.h4 [ ] [ str model.Location.Location.Region ]
                Heading.h4 [ ] [ sprintf "%.1fKM to London" model.Location.DistanceToLondon |> str ]
            ]
        ]


/// The view function knows how to render the UI given a model, as well as to dispatch new messages based on user actions.
let view model dispatch =
    div [] [
        Hero.hero [ Hero.Color Color.IsInfo ] [
            Hero.body [ ] [
                Container.container [ Container.IsFluid
                                      Container.Modifiers [ Modifier.TextAlignment (Screen.All, TextAlignment.Centered) ] ] [
                    Heading.h1 [ ] [
                        str "UK Location Data Mashup"
                    ]
                ]
            ]
        ]

        Container.container [] [
            yield
                Field.div [] [
                    Label.label [] [ str "Postcode" ]
                    Control.div [ Control.HasIconLeft; Control.HasIconRight ] [
                        Input.text
                            [ Input.Placeholder "Ex: EC2A 4NE"
                              Input.Value model.Postcode
                              Input.Modifiers [ Modifier.TextTransform TextTransform.UpperCase ]
                              Input.Color (if model.ValidationError.IsSome then Color.IsDanger else Color.IsSuccess)
                              Input.Props [ OnChange (fun ev -> dispatch (PostcodeChanged !!ev.target?value)); onKeyDown KeyCode.enter (fun _ -> dispatch GetReport) ] ]
                        Fulma.Icon.icon [ Icon.Size IsSmall; Icon.IsLeft ] [ Fa.i [ Fa.Solid.Home ] [] ]
                        (match model with
                         | { ValidationError = Some _ } ->
                            Icon.icon [ Icon.Size IsSmall; Icon.IsRight ] [ Fa.i [ Fa.Solid.Exclamation ] [] ]
                         | { ValidationError = None } ->
                            Icon.icon [ Icon.Size IsSmall; Icon.IsRight ] [ Fa.i [ Fa.Solid.Check ] [] ])
                    ]
                    Help.help
                       [ Help.Color (if model.ValidationError.IsNone then IsSuccess else IsDanger) ]
                       [ str (model.ValidationError |> Option.defaultValue "") ]
                ]
            yield
                Field.div [ Field.IsGrouped ] [
                    Level.level [ ] [
                        Level.left [] [
                            Level.item [] [
                                Button.button
                                    [ Button.IsFullWidth
                                      Button.Color IsPrimary
                                      Button.OnClick (fun _ -> dispatch GetReport)
                                      Button.Disabled (model.ValidationError.IsSome)
                                      Button.IsLoading (model.ServerState = ServerState.Loading) ]
                                    [ str "Submit" ] 
                                
                                Button.button
                                    [ Button.IsFullWidth
                                      Button.Color IsPrimary
                                      Button.OnClick (fun _ -> dispatch Clear) ]
                                    [ str "Clear" ] ] ] ] 

                ]

            match model with
            | { Report = None; ServerState = (Idle | Loading) } -> ()
            | { ServerState = ServerError error } ->
                yield
                    Field.div [] [
                        Tag.list [ Tag.List.HasAddons; Tag.List.IsCentered ] [
                            Tag.tag [ Tag.Color Color.IsDanger; Tag.Size IsMedium ] [
                                str error
                            ]
                        ]
                    ]
            | { Report = Some model } ->
                yield
                    Tile.ancestor [ ] [
                        Tile.parent [ Tile.Size Tile.Is12 ] [
                            bingMapTile model.Location.Location.LatLong
                        ]
                    ]
                yield
                    Tile.ancestor [ ] [
                        Tile.parent [ Tile.IsVertical; Tile.Size Tile.Is4 ] [
                            locationTile model
                            weatherTile model.Weather
                        ]
                        Tile.parent [ Tile.Size Tile.Is8 ] [
                            crimeTile model.Crimes
                        ]
                  ]
        ]

        br [ ]

        Footer.footer [] [
            Content.content
                [ Content.Modifiers [ Fulma.Modifier.TextAlignment(Screen.All, TextAlignment.Centered) ] ]
                [ safeComponents ]
        ]
    ]