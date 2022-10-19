export class ChrResultModel{
  chrNummer: string;
  ejendom: Ejendom;
}

export class Ejendom {
  adresse: string;
  kommuneNavn: string;
  postNummer: string;
  postDistrikt: string;
  byNavn: string;
}
