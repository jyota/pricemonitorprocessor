module PriceMonitorProcessor.Models


[<CLIMutable>]
type MonitorRequestAction =
    {
        Id : int64
        ActionId : int16
        ActionTriggerId : int16
        ActionTriggerThreshold : decimal
        ThresholdTypeId : int16
        ActionTargetText : string
    }


[<CLIMutable>]
type MonitorRequest =
    {
        Id : int64
        Url : string
        TargetPrice : decimal
        RequestingUserId : int64
        MonitorRequestActions : array<MonitorRequestAction>
    }
