#r @"..\packages\Akka.1.3.0\lib\net45\Akka.dll"

open Akka.Actor
open Akka.Routing
open Akka.Event
open System.Collections.Concurrent
open System

type Worker(stat: ConcurrentDictionary<int, int>, messages: ConcurrentDictionary<int, int>) as self =
    inherit ReceiveActor()
    let log = Logging.GetLogger(Worker.Context)
    let rnd = Random()

    do self.Receive<int>(fun msg ->
           //printfn "[%O, %d] Received %s!" self (hash self) msg
           //log.Info(sprintf "Received %s!" msg)
           stat.AddOrUpdate(hash self, (fun k -> 1), fun k n -> n + 1) |> ignore
           let r = rnd.Next(0, 10)
           if r % 3 = 0 then failwithf "Bad random value %d!" r
           messages.AddOrUpdate(msg, (fun _ -> 1), (fun k n -> n + 1)) |> ignore
       )

    member __.Sender: IActorRef = base.Sender

let system = ActorSystem.Create "system"
let stat = ConcurrentDictionary<int, int>()
let messages = ConcurrentDictionary<int, int>()
[1..10] |> List.iter (fun x -> messages.[x] <- 0)

let workerPool = 
    system.ActorOf(
        Props.Create<Worker>(stat, messages)
             .WithRouter(SmallestMailboxPool 10)
             .WithSupervisorStrategy(OneForOneStrategy.DefaultStrategy),
        "workers")

messages.Keys |> Seq.toList |> List.iter workerPool.Tell

for k, v in messages |> Seq.map (fun (KeyValue(k, v)) -> k, v) |> Seq.sortByDescending snd do
    printfn "message %9d has been processed %3d times" k v

for k, v in stat |> Seq.map (fun (KeyValue(k, v)) -> k, v) |> Seq.sortByDescending snd do
    printfn "actor %9d got %3d messages" k v
