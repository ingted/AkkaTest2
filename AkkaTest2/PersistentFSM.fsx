#r @"..\packages\Newtonsoft.Json.9.0.1\lib\net45\Newtonsoft.Json.dll"
#r @"..\packages\Akka.1.3.0\lib\net45\Akka.dll"
#r @"..\packages\Akka.Persistence.1.3.0\lib\net45\Akka.Persistence.dll"

open Akka.Actor
open Akka.Persistence
open Akka.Persistence.Fsm
open System

type Item = 
    { Id: string 
      Name: string 
      Price: float }

type Command =
    | AddItem of Item
    | Buy
    | Leave
    | GetCurrentCart

type IUserState = inherit PersistentFSM.IFsmState

type UserState =
    | LookingAround
    | Shopping
    | Inactive
    | Paid
    interface IUserState with
        member this.Identifier =
            match this with
            | LookingAround -> "Looking Around"
            | Shopping -> "Shopping"
            | Inactive -> "Inactive"
            | Paid -> "Paid"

type DomainEvent =
    | ItemAdded of Item
    | OrderExecuted
    | OrderDiscarded

type ShoppingCart =
    | Empty
    | NonEmpty of Item list
    member this.AddItem item = 
        match this with
        | Empty -> NonEmpty [item]
        | NonEmpty items -> NonEmpty (item :: items)

type ReportEvent =
    | PurchaseWasMade of seq<Item>
    | ShoppingCardDiscarded

type T() as self =
    inherit PersistentFSM<UserState, ShoppingCart, DomainEvent>()
    let reportActor: IActorRef = null

    let (=>) (state: UserState) (f: FSMBase.Event<ShoppingCart> -> _) = self.When(state, fun evt _ -> f evt)

    do self.StartWith(LookingAround, Empty)
       
       LookingAround => fun evt ->
           match evt.FsmEvent with
           | :? Command as cmd ->
                match cmd with
                | AddItem item ->
                    self.GoTo(Shopping)
                        .Applying(ItemAdded item)
                        .ForMax(TimeSpan.FromSeconds 1.)
                | GetCurrentCart ->
                    self.Stay().Replying(evt.StateData)
                | _ -> self.Stay()
           | _ -> self.Stay()

       Shopping => fun evt ->
           match evt.FsmEvent with
           | :? Command as cmd ->
                match cmd with
                | AddItem item ->
                    self.Stay()
                        .Applying(ItemAdded item)
                        .ForMax(TimeSpan.FromSeconds 1.)
                | Buy ->
                    self.GoTo(Paid).Applying(OrderExecuted)
                        .AndThen(fun cart ->
                            match cart with
                            | NonEmpty items ->
                                reportActor.Tell(PurchaseWasMade items)
                                self.SaveStateSnapshot()
                            | Empty ->
                                self.SaveStateSnapshot())
                | Leave ->
                    self.Stop().Applying(OrderDiscarded)
                        .AndThen(fun cart ->
                           reportActor.Tell(ShoppingCardDiscarded)
                           self.SaveStateSnapshot())
                | GetCurrentCart ->
                    self.Stay().Replying(evt.StateData)
           | :? FSMBase.StateTimeout ->
               self.GoTo(Inactive).ForMax(TimeSpan.FromSeconds 2.)
           | _ -> self.Stay()

       Inactive => fun evt ->
           match evt.FsmEvent with
           | :? Command as cmd ->
               match cmd with
               | AddItem item ->
                   self.GoTo(Shopping)
                       .Applying(ItemAdded item)
                       .ForMax(TimeSpan.FromSeconds 1.)
               | _ -> self.Stay()
           | :? FSMBase.StateTimeout ->
               self.Stop()
                   .Applying(OrderDiscarded)
                   .AndThen(fun cart -> reportActor.Tell ShoppingCardDiscarded)
           | _ -> self.Stay()

       Paid => fun evt ->
           match evt.FsmEvent with
           | :? Command as cmd ->
               match cmd with
               | Leave -> self.Stop()
               | GetCurrentCart -> self.Stay().Replying(evt.StateData)
               | _ -> self.Stay()
           | _ -> self.Stay()

    override __.ApplyEvent (evt, cartBeforeEvent) =
        match evt with
        | ItemAdded item -> cartBeforeEvent.AddItem item
        | OrderExecuted -> cartBeforeEvent
        | OrderDiscarded -> Empty

    override __.PersistenceId = "id"
    override __.ReceiveRecover _ = false

let system = ActorSystem.Create "system"


