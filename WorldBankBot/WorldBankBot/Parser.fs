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
    
    let trim (s:string) = s.Trim ()

    let pCountry = 
        pstring "COUNTRY" >>. spaces >>. basicString
        |>> (trim >> COUNTRY)

    let pCountries =
        pstring "COUNTRIES" >>. spaces >>. pList
        |>> ((List.map trim) >> COUNTRIES)

    let pPlace = (pCountry <|> pCountries)

    let pIndicator =
        pstring "INDICATOR" >>. spaces >>. basicString
        |>> (trim >> INDICATOR)
 
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

    type Res<'a> =
        | OK of 'a 
        | Fail of string

    let extractArguments (exp:string) =         
        match (run pArgs exp) with
        | Success(arg,_,_) -> OK(arg)
        | Failure(msg,_,_) -> Fail(msg)