# IsItKrampus.NET

This project is my entry for this years [F# Advent Calendar](https://sergeytihon.com/2020/10/22/f-advent-calendar-in-english-2020/). You can find my article describing this project there or directly [on my blog](https://www.gregorbeyerle.me/posts/2020/12/25/save-yourself-from-krampus-with-ml-net-and-f-sharp).

## What you'll need

If you want to play around with the code yourself it would make sense to have:

- The current .NET 5 SDK
- Visual Studio Code (Insiders) with the Ionide and .NET Interactive plugins
- NodeJS (at least a current LTS) and npm
- Docker (Linux containers - ARM64 images)
- A modern Browser (Chrome, Firefox, "Edgium", Brave, etc.)

When you have everything you need don't forget to call `dotnet tool restore` in the root of this repository.

## What's included

If you read the blog post you might already have a very good idea about the topic, the general process and the results of each step. If you haven't read the blog post here is the tl;dr:

- Objective: build a computer vision model, that correctly classifies Santa, Krampus or any Other person
- Collect a large enough dataset, containing images from all three classes while trying to de-bias the Other class
- Document collected URIs and downloaded images in a way, that makes the process reproducible
- Create an auxillary app to crop and label the images and use it on the dataset
- Document the steps taken to process the data set to make the process reproducible
- Use ML.NET to build a multi-class image classifier using transfer learning with an Inception V3 model
- Use the trained model in a local Docker image based AWS Lambda function and call it from a Bolero App

Each step is connected to one or more artifacts. All artifacts are described in the following paragraphs.

## Data Collection

Data collection is documented in [DataCollection.ipynb](./notebooks/DataCollection.ipynb). The interactive notebook uses Canopy to scrape Google Image Search for images related to a specified search term.

⚡⚠⚡ Be aware, that .NET Interactive notebooks mostly use absolute paths because relative paths don't work as well. Change the paths according to your system ⚡⚠⚡

## Data Preparation

The application I build to prepare the data can be found in the `IsItKrampus.NET.DataSet.*` projects. They include:

- A [Fable](https://fable.io/) web frontend
- A [Giraffe](https://github.com/giraffe-fsharp/Giraffe) backend application
- A [Fable.Remoting](https://github.com/Zaid-Ajaj/Fable.Remoting) RPC shared project

I didn't come around to write better automation so you might want to open two console windows to work with this project.

### Backend

- Navigate to the `IsItKrampus.NET.DataSet.Server` directory
- Execute `dotnet build`
- Execute `dotnet run`

### Frontend

- Navigate to the `IsItKrampus.NET.DataSet.Client` directory
- Execute `npm install`
- Execute `npm start`

You can now access the application on [http://localhost:8080/](http://localhost:8080/).

## Modelling

The modelling process is documented in [Modelling.ipynb](./notebooks/Modelling.ipynb). The interactive notebook uses ML.NET and ImageSharp to load the dataset, augment it and train a image classifier. Additionally there is code to save the trained model and also code to show how to load it again and test it on random samples. ⚠ You'll need to train the model or download it from the releases artifacts to build and use the AWS Lambda function image ⚠

⚡⚠⚡ Be aware, that .NET Interactive notebooks mostly use absolute paths because relative paths don't work as well. Change the paths according to your system ⚡⚠⚡

## Inference application

The application I build to demo one possible usage scenario of trained model can be found in the `IsItKrampus.NET.App.*` repositories. Currently it isn't accessible on the public internet (because I didn't manage to nail the image based AWS Lambda function deployment behind a HTTP trigger). You can run it locally though using local Docker tooling for AWS and the plain old .NET 5 SDK for the web frontend. It might be a good idea to work with two console windows for this app as well.

### AWS Lambda Function

- Make sure you have a trained model called `model.zip` in the `models` directory
- Navigate to the `IsItKrampus.NET.App.Backend` directory
- Execute `dotnet publish -c Release -r linux-x64`
- Execute `docker build -t isitcrampus-lambda:latest .`
- Execute `docker-compose up`

You should now see two images starting: one nginx reverse proxy (configured to set CORS headers) and the lambda function.

### Bolero Web Frontend

- Navigate to the `IsItKrampus.NET.App.Frontend.DevServer` directory (server component only used to host the Bolero web app)
- Execute `dotnet build`
- Execute `dotnet run`

You can now access the web frontend via [https://localhost:5001/](https://localhost:5001/). If you have problems using HTTPS because you can't work with self signed developer certificates you can also reach it via [http://localhost:5000/](http://localhost:5000/).

⚡⚠⚡ For some reason loading and resizing images using ImageSharp takes a pretty long while in WebAssembly. I have tried to find the reason for this behavior but I haven't had much luck yet. Please be advised to go for smaller images if you want to move quickly ⚡⚠⚡

## Feedback

I'm always happy to receive feedback. If you have questions regarding the project or the blog post don't hesitate to open an issue.

Merry Christmas and happy holidays, y'all!
