namespace WorldBankBot

module Core =

    type PLACE =
        | COUNTRY of string
        | COUNTRIES of string list

    type MEASURE =
        | INDICATOR of string

    type TIMEFRAME = 
        | OVER of int * int
        | IN of int
