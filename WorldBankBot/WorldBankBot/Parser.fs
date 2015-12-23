namespace WorldBankBot

module Parser =

    open FParsec
    open WorldBankBot.Core

    let reservedChars = set [';';'[';']';'*']

    let basicString =
        let normalChar = satisfy (reservedChars.Contains >> not)
        spaces >>. manyChars (normalChar)

    let pList =
        pstring "[" >>. sepBy basicString (pstring ";") .>> pstring "]" .>> spaces

    let pCountry = 
        pstring "COUNTRY" >>. spaces >>. basicString
        |>> COUNTRY

    let pCountries =
        pstring "COUNTRIES" >>. spaces >>. pList
        |>> COUNTRIES

    let pPlace = (pCountry <|> pCountries)

    let pIndicator =
        pstring "INDICATOR" >>. spaces >>. basicString
        |>> INDICATOR
 
    let pYear = spaces >>. pint32 .>> spaces

    let pYears = 
        tuple2 pYear (pstring "-" >>. pYear)
     
    let pOver = 
        pstring "OVER" >>. pYears
        |>> OVER

    let pIn =
        pstring "IN" >>. pYear
        |>> IN

    let pTimeframe = pOver <|> pIn

    let pArgs = 
        tuple3 
            (spaces >>. pPlace .>> (pchar '*' >>. spaces)) 
            (pIndicator .>> (pchar '*' >>. spaces)) 
            pTimeframe

    let extractArguments (exp:string) =         
        match (run pArgs exp) with
        | Success(x,_,_) -> Some(x)
        | Failure(_) -> None