/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Infrastructure.Data.Seed.Data
{
	using System.Collections.Generic;

	public static class BackendConfigurationSeedEforms
	{
		public static IEnumerable<string> EformsSeed => new[]
		{
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142244</Id>
	<Repeated>0</Repeated>
	<Label>01. Elforbrug|01. Electricity</Label>
	<StartDate>2021-01-11</StartDate>
	<EndDate>2031-01-11</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142244</Id>
			<Label>01. Elforbrug|01. Electricity</Label>
			<Description>
				<![CDATA[Aflæs elmåler|Read electricity meter]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372408</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372409</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Eksempler på energibesparende foranstaltninger:<br>• Frekvensstyrede elmotorer til dele af staldriften, herunder ventilationen.<br>• Staldbelysning er kun tændt efter behov.<br>• Temperaturreguleret styringssystem til ventilation som sikrer, at ventilationen kører optimalt i forhold til staldtemperatur og elforbruget. Dette sikrer temperaturstyring og minimumsventilation i perioder hvor behovet for ventilation er lavt.<br>• Isolere bygninger, installere energibesparende belysning og anvende naturlig ventilation i videst muligt omfang.<br>|<br>Examples of energy saving measures:<br>• Frequency-controlled electric motors for parts of stable operation, including ventilation.<br>• Stable lighting is only switched on as needed.<br>• Temperature-controlled control system for ventilation which ensures that the ventilation runs optimally in relation to barn temperature and electricity consumption. This ensures temperature control and minimum ventilation during periods when the need for ventilation is low.<br>• Insulate buildings, install energy-saving lighting and use natural ventilation as much as possible.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Number"">
					<Id>372407</Id>
					<Label>Elmåler (kWh)|Electricity meter (kWh)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372405</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372404</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372406</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142242</Id>
	<Repeated>0</Repeated>
	<Label>01. Vandforbrug|01. Water comsumption</Label>
	<StartDate>2021-01-11</StartDate>
	<EndDate>2031-01-11</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142242</Id>
			<Label>01. Vandforbrug|01. Water comsumption</Label>
			<Description>
				<![CDATA[Aflæs vandmåler|Read water meter]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372393</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372394</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Eksempler på vandbesparende foranstaltninger:<br>• Ved rengøring af staldene i blødsættes staldene forud for egentlig vask med højtryksrenser. Både i blødsætning og højtryksrensning er vandbesparende.<br>• Staldene kontrolleres dagligt og ved evt. lækager udføres der straks små reparationer eller der tilkaldes service, hvis der er behov for dette.<br>• Løbende kalibrering af drikkevandsinstallationerne.<br>|<br>Examples of water saving measures:<br>• When cleaning the stables, the stables are soaked prior to actual washing with a high-pressure cleaner. Soaking the stables and high pressure cleaning are water saving.<br>• The stables are checked daily and in the event of any leaks, small repairs are carried out immediately or service is called in if necessary.<br>• Continuous calibration of the drinking water installations.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Number"">
					<Id>372392</Id>
					<Label>Vandmåler (m3)|Water meter (m3)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372390</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372389</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372391</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142266</Id>
	<Repeated>0</Repeated>
	<Label>02. Brandudstyr|02. Fire equipment</Label>
	<StartDate>2021-01-17</StartDate>
	<EndDate>2031-01-17</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142266</Id>
			<Label>02. Brandudstyr|02. Fire equipment</Label>
			<Description>
				<![CDATA[Kontrolpunkter brandudstyr|Checkpoints fire equipment]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372552</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372553</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Alle skal vide, hvor brandudstyret er placeret, og hvordan det håndteres.<br><br>Der skal løbende foretages en kontrol og vedligeholdelse af brandudstyret, så det altid er funktionsdygtigt.&nbsp;<br>|<br>Everyone needs to know where the fire equipment is located and how it is handled.<br><br>The fire equipment must be checked and maintained on an ongoing basis so that it is always functional.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>372551</Id>
					<Label>Medarbejder kender placering af brandudstyr|Employee knows the location of fire equipment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372554</Id>
					<Label>Relvant brandudstyr til rådighed|Relevant fire equipment available</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372555</Id>
					<Label>Brandudstyr er ikke udløbet|Fire equipment has not expired</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372549</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372548</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372550</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142269</Id>
	<Repeated>0</Repeated>
	<Label>02. Førstehjælp|02. First aid</Label>
	<StartDate>2021-01-17</StartDate>
	<EndDate>2031-01-17</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142269</Id>
			<Label>02. Førstehjælp|02. First aid</Label>
			<Description>
				<![CDATA[Kontrolpunkter for førstehjælpsudstyr|Checkpoint first aid equipment]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372576</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372577</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Det er ikke et lovkrav – men det anbefales, at alle ved, hvor førstehjælpskassen er placeret.<br>|<br>It is not a legal requirement - but it is recommended that everyone knows where the first aid kit is located.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>372575</Id>
					<Label>Mængde af førstehjælpsudstyr OK|Amount of first aid equipment OK</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372578</Id>
					<Label>Medarbejder kender placering af førstehjælpskasse|Employee knows the location of the first aid kit</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372573</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372572</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372574</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142267</Id>
	<Repeated>0</Repeated>
	<Label>02. Sikkerhedsudstyr/værnemidler|02. Safety equipment_protective equipment</Label>
	<StartDate>2021-01-17</StartDate>
	<EndDate>2031-01-17</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142267</Id>
			<Label>02. Sikkerhedsudstyr/værnemidler|02. Safety equipment_protective equipment</Label>
			<Description>
				<![CDATA[Kontrolpunkter sikkerhedsudstyr/værnemidler|Checkpoints safety equipment/protective equipment]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372560</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372561</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[For at minimere arbejdsulykker og andre skadelige virkninger på miljøet, skal der være opsat værnemidler på husdyrbruget.<br><br>Forskellige arbejdsopgaver kræver forskellige værnemidler, og alle medarbejderne skal være bekendte med disse.<br>|<br>In order to minimize accidents at work and other harmful effects on the environment, protective equipment must be installed on livestock farms.<br><br>Different work tasks require different protective equipment, and all employees must be familiar with these.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>372559</Id>
					<Label>Relevante værnemidler til rådighed|Relevant protective equipment available</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372562</Id>
					<Label>Medarbejder ved hvor værnemidler findes|Employee knows where protective equipment is available</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372563</Id>
					<Label>Medarbejder er bekendt med korrekt brug af værnemidler|Employee is familiar with the correct use of protective equipment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372557</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372556</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372558</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142142</Id>
	<Repeated>0</Repeated>
	<Label>03. Kontrol flydelag|03. Control floating layer</Label>
	<StartDate>2020-10-06</StartDate>
	<EndDate>2030-10-06</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142142</Id>
			<Label>03. Kontrol flydelag|03. Control floating layer</Label>
			<Description>
				<![CDATA[Kontrollér flydelag og angiv evt. årsag til manglende flydelag.|Check floating layer and indicate - if necessary - cause of lack of floating layer.<br>]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372222</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372223</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Hvis der ikke er fast overdækning på en gyllebeholder, skal der etableres flydelag på gylleoverfladen.&nbsp;<br><br>Det skal altid sikres, at der er et tæt flydelag. Flydelaget begrænser ammoniakfordampningen, hvilket giver en bedre gødningsværdi i gyllen.<br><br>Flydelaget skal dække hele beholderens overflade, dog må der gerne være en brudflade op til 3 m2 ved det dykkede indløb.&nbsp;<br><br>Flydelaget kan etableres med en fast gødning eller fx snittet halm.<br><br>Hvis kommunen konstaterer mangelfuldt flydelag ved to tilsyn indenfor 3 år, skal overdækning påbydes, undtagen i særlige tilfælde, hvis kommunen ud fra en konkret vurdering finder, at overtrædelsen er undskyldelig.<br>|<br>If there is no fixed cover on a slurry tank, floating layers must be established on the slurry surface.<br><br>It must always be ensured that there is a tight floating layer. The floating layer limits the ammonia evaporation, which gives a better fertilizer value in the manure.<br><br>The floating layer must cover the entire surface of the container, however, there may be a breaking surface up to 3 m2 at the submerged inlet.<br><br>The floating layer can be established with a solid fertilizer or, for example, cut straw.<br><br>If the municipality finds a defective floating layer during two inspections within 3 years, cover must be imposed, except in special cases if the municipality, based on a specific assessment, finds that the violation is excusable.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>371625</Id>
					<Label>Flydelag OK|Floating layer OK</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>371900</Id>
					<Label>Vælg årsag til manglende flydelag|Select reason for lack of floating layer</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[Beholder omrørt|Slurry tank stirred]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[Gylle udbragt|Slurry delivered]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Beholder tom|Slurry tank empty]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>4</Key>
							<Value>
								<![CDATA[Halm tilført|Straw added]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>4</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>5</Key>
							<Value>
								<![CDATA[Flyttet til anden beholder|Moved to another slurry tank]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>5</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>6</Key>
							<Value>
								<![CDATA[Modtaget biogas-gylle|Biogas slurry received]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>6</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>371627</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>371628</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>371629</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142060</Id>
	<Repeated>0</Repeated>
	<Label>03. Kontrol konstruktion|03. Control construction</Label>
	<StartDate>2020-08-07</StartDate>
	<EndDate>2030-08-07</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142060</Id>
			<Label>03. Kontrol konstruktion|03. Control construction03. Контрольна конструкції</Label>
			<Description>
				<![CDATA[Tjek for skader på kablerne, kabelomslutningen og fugemassen.|Check for damage to the cables, cable enclosure and sealant.]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372224</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372225</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Det skal altid sikres, at gyllebeholderne, fortanke og opsamlingsbeholderne har den fornødne styrke og tæthed, så udslip forhindres. Minimum en gang om året kontrolleres beholderne for skader.<br><br>Tjek for skader på kablerne. Kabler under jorden kan kun inspiceres ved at grave, og de bør derfor ikke inspiceres, med mindre der er mistanke om lækage.<br><br>Tjek om der er huller på kabelomslutningen. Det kan oftest ses ved, at fedtet er løbet ud af omslutningen og har skabt mørke pletter på beholderen.<br><br>Fugemassen kan være mørnet, hvilket du også skal tjekke.<br><br>Få udbedret eventuelle skader med det samme af en sagkyndig.<br>|<br>It must always be ensured that the slurry tanks, pre-tanks and collection tanks have the necessary strength and tightness, so that spills are prevented. Check the containers for damage at least once a year.<br><br>Check for damage to the cables. Underground cables can only be inspected by digging and should therefore not be inspected unless leakage is suspected.<br><br>Check for holes in the cable cover. This can most often be seen by the fact that the grease has run out of the enclosure and has created dark spots on the slurry tank.<br><br>The sealant may be softened, which you should also check.<br><br>Have any damage repaired immediately by an expert.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>371011</Id>
					<Label>Kabler OK|Cables OK</Label>
					<Description>
						<![CDATA[Der må ikke være synlige skader på kablerne.|There must be no visible damage to the cables.]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372047</Id>
					<Label>Kabelomslutning OK|Cable enclosure OK</Label>
					<Description>
						<![CDATA[Huller på kabelomslutning kan oftest ses ved, at fedtet er løbet ud af omslutningen og har skabt mørke pletter på beholderen.|Holes on the cable enclosure can most often be seen by the grease running out of the enclosure and creating dark stains on the container.]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372048</Id>
					<Label>Fugemassen OK|Sealant OK</Label>
					<Description>
						<![CDATA[Fugemassen må ikke være mørnet.|The sealant must not be softened.]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>371014</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>371015</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>371016</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
  <Id>142180</Id>
  <Repeated>0</Repeated>
  <Label>03. Kontrol alarmanlæg gyllebeholder|03. Check alarm system slurry tank</Label>
  <StartDate>2020-11-11</StartDate>
  <EndDate>2030-11-11</EndDate>
  <Language>da</Language>
  <MultiApproval>false</MultiApproval>
  <FastNavigation>false</FastNavigation>
  <Review>false</Review>
  <Summary>false</Summary>
  <DisplayOrder>0</DisplayOrder>
  <ElementList>
    <Element type=""DataElement"">
      <Id>142180</Id>
      <Label>03. Kontrol alarmanlæg gyllebeholder|03. Check alarm system slurry tank</Label>
      <Description><![CDATA[Test alarmanlæg på gyllebeholder.|Check alarm system slurry tank.<br>]]></Description>
      <DisplayOrder>0</DisplayOrder>
      <ReviewEnabled>false</ReviewEnabled>
      <ManualSync>false</ManualSync>
      <ExtraFieldsEnabled>false</ExtraFieldsEnabled>
      <DoneButtonDisabled>true</DoneButtonDisabled>
      <ApprovalEnabled>false</ApprovalEnabled>
      <DataItemGroupList>
        <DataItemGroup type=""FieldGroup"">
          <Id>372228</Id>
          <Label>LÆS MERE|READ MORE</Label>
          <Description><![CDATA[]]></Description>
          <DisplayOrder>0</DisplayOrder>
          <Value>Closed</Value>
          <Color>fff6df</Color>
          <DataItemList>
            <DataItem type=""None"">
              <Id>372229</Id>
              <Label>INFO|INFO</Label>
              <Description><![CDATA[Kontrol af alarmsystem gennemføres ved fx at trække trykmålerne op ca. ½ meter over gylleoverfladen og vent et par minutter. Herefter skal der komme en alarm. Rens sensoren.<br><br>Vær opmærksom på, at trykmålerne kan tage skade ved omrøring. Trykmålerne kan evt. tages op, når der omrøres.<br>|<br>Checking of the alarm system is carried out by, for example, pulling up the pressure gauges approx. ½ meters above the slurry surface and wait a few minutes. Then there should be an alarm. Clean the sensor.<br><br>Be aware that the pressure gauges can be damaged by stirring. The pressure gauges can possibly taken up when stirred.]]></Description>
              <DisplayOrder>0</DisplayOrder>
              <Color>fff6df</Color>
            </DataItem>
          </DataItemList>
        </DataItemGroup>
      </DataItemGroupList>
      <DataItemList>
        <DataItem type=""CheckBox"">
          <Id>371947</Id>
          <Label>Alarmanlæg OK|Alarm system OK</Label>
          <Description><![CDATA[]]></Description>
          <DisplayOrder>1</DisplayOrder>
          <Selected>false</Selected>
          <Mandatory>false</Mandatory>
          <Color>e8eaf6</Color>
        </DataItem>
        <DataItem type=""Picture"">
          <Id>371945</Id>
          <Label>Billede|Picture</Label>
          <Description><![CDATA[]]></Description>
          <DisplayOrder>2</DisplayOrder>
          <Mandatory>false</Mandatory>
          <Color>e8eaf6</Color>
        </DataItem>
        <DataItem type=""Comment"">
          <Id>371944</Id>
          <Label>Kommentar|Comment</Label>
          <Description><![CDATA[]]></Description>
          <DisplayOrder>3</DisplayOrder>
          <Multi>1</Multi>
          <GeolocationEnabled>false</GeolocationEnabled>
          <Split>false</Split>
          <Value/>
          <ReadOnly>false</ReadOnly>
          <Mandatory>false</Mandatory>
          <Color>e8eaf6</Color>
        </DataItem>
        <DataItem type=""SaveButton"">
          <Id>371946</Id>
          <Label>Gem registrering|Save registration</Label>
          <Description><![CDATA[]]></Description>
          <DisplayOrder>4</DisplayOrder>
          <Value>GEM|SAVE</Value>
          <Color>f0f8db</Color>
        </DataItem>
      </DataItemList>
    </Element>
  </ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142289</Id>
	<Repeated>0</Repeated>
	<Label>04. Foderindlægssedler|04. Feeding documentation</Label>
	<StartDate>2021-02-02</StartDate>
	<EndDate>2031-02-02</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142289</Id>
			<Label>04. Foderindlægssedler|04. Feeding documentation</Label>
			<Description>
				<![CDATA[Tag billeder af foderindlægssedlerne.|Take pictures of the feed package leaflets.]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372775</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372776</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Tag billeder af foderindlægssedlerne.|Take pictures of the feed package leaflets.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Picture"">
					<Id>372772</Id>
					<Label>Billede af foderindlægssedler|Picture of feed leaflets</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372771</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372773</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>8</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142288</Id>
	<Repeated>0</Repeated>
	<Label>05. Stald_klargøring|05. Stable_prep</Label>
	<StartDate>2021-01-31</StartDate>
	<EndDate>2031-01-31</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142288</Id>
			<Label>05. Stald_klargøring|05. Stable_prep</Label>
			<Description>
				<![CDATA[Klargøringstjek af stald|Preparation check of stable<br>]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372762</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372763</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Klargøringstjek af stald|Preparation check of stable]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>372764</Id>
					<Label>1. Afprøv ventil i vandkop|1. Test valve in water cup</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373061</Id>
					<Label>2. Afprøv ventilationsanlæg (min. og max. indstilling)|2. Test ventilation system (min. and max. setting)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372766</Id>
					<Label>3. Gennemfør følertest|3. Perform sensor tests</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372765</Id>
					<Label>4. Afprøv og indstil foderautomat|4. Test and set the feeder</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372767</Id>
					<Label>5. Tjek at legetøj er nyt/rent|5. Check that toys are new/clean</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372768</Id>
					<Label>6. Tjek overbrusning og rens evt. filter|6. Check sprinkling and clean - if necessary - filter</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372769</Id>
					<Label>7. Tjek at alarm virker|7. Check that the alarm works</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>374039</Id>
					<Label>8. Vask udsugning|8. Wash exhaust</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>8</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372759</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>9</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372758</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>10</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372760</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>11</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142226</Id>
	<Repeated>0</Repeated>
	<Label>06. Siloer|06. Silos</Label>
	<StartDate>2020-12-28</StartDate>
	<EndDate>2030-12-28</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142226</Id>
			<Label>06. Siloer|06. Silos</Label>
			<Description>
				<![CDATA[Kontrol af foderspild og rørsamlinger|Control of feed spills and pipe joints<br><br>]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372308</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372309</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Opbevaring af foder skal ske på en måde, så tilhold af skadedyr forebygges, og spild ikke giver anledning til punktforurening.  <br><br>Dette forebygges ved at sikre, at foder opbevares i tætte og dertil egnede beholdere.<br><br>Der vil kunne ske spild af foder fx ved utætte samlinger, levering/indblæsning af foder eller lignende. Sker der spild af foder, er det vigtigt, at der løbende bliver samlet op, så fx tilhold af rotter undgås.|<br>Feed must be stored in such a way that pests are prevented and spills do not give rise to point pollution.<br><br>This is prevented by ensuring that feed is stored in tight and suitable containers.<br><br>There may be a waste of feed, for example by leaky joints, delivery / supply of feed or the like. If there is a waste of feed, it is important that it is collected regularly, so that, for example, the presence of rats is avoided.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>372307</Id>
					<Label>Ingen foderspild|No feed spills</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372310</Id>
					<Label>Rørsamlinger tætte|Pipe joints tight</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372305</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372304</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372306</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142271</Id>
	<Repeated>0</Repeated>
	<Label>07. Fluer|07. Flies</Label>
	<StartDate>2021-01-17</StartDate>
	<EndDate>2031-01-17</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142271</Id>
			<Label>07. Fluer|07. Flies</Label>
			<Description>
				<![CDATA[Tag billede af serviceaftale/faktura|Take picture of service agreement/invoice]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372587</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372588</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[De vigtigste punkter ved bekæmpelse af fluer er god gødningshåndtering og en generel god staldhygiejne.<br><br>Når du tager et billede af din serviceftale og/eller faktura på fluebekæmpelse, er den nem at finde frem på web, når du har brug for at kunne fremvise denne til f.eks. tilsynsmyndighederne.<br>|<br>The most important points when controlling flies are good manure handling and a generally good stable hygiene.<br><br>When you take a picture of your service agreement and / or invoice for fly control, it is easy to find on the web when you need to be able to present it to e.g. supervisory authorities.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Picture"">
					<Id>372585</Id>
					<Label>Billede serviceaftale/faktura|Picture service agreement/invoice</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372584</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372586</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142270</Id>
	<Repeated>0</Repeated>
	<Label>07. Rotter|07. Rats</Label>
	<StartDate>2021-01-17</StartDate>
	<EndDate>2031-01-17</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142270</Id>
			<Label>07. Rotter|07. Rats</Label>
			<Description>
				<![CDATA[Tag billede af serviceaftale/faktura|Take picture of service agreement/invoice]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372582</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372583</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Effektive virkemidler mod rotter er orden, ryddelighed, fejede gulve, fjernelse af vegetation langs bygninger, reparation af huller/rørgennemføringer, at døre/porte for så vidt muligt holdes lukkede og at oplag af “diverse” sættes på paller og strøer.<br><br>Når du tager et billede af din serviceftale og/eller faktura på rottebekæmpelse, er den nem at finde frem på web, når du har brug for at kunne fremvise denne til f.eks. tilsynsmyndighederne.<br>|<br>Effective measures against rats are order, tidiness, swept floors, removal of vegetation along buildings, repair of holes / pipe penetrations, that doors / gates are kept closed as far as possible and that storage of ""miscellaneous"" is placed on pallets and joists.<br><br>When you take a picture of your service agreement and / or invoice for rat control, it is easy to find on the web when you need to be able to present it to e.g. supervisory authorities.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Picture"">
					<Id>372580</Id>
					<Label>Billede serviceaftale/faktura|Picture service agreement/invoice</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372579</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372581</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142589</Id>
	<Repeated>0</Repeated>
	<Label>08. Luftrensning driftsstop|08. Air cleaning downtime</Label>
	<StartDate>2021-09-08</StartDate>
	<EndDate>2031-09-08</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142589</Id>
			<Label>08. Luftrensning driftsstop|08. Air cleaning downtime</Label>
			<Description>
				<![CDATA[Logbog driftsstop|Logbook downtime]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>375175</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>375176</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Bruges som ammoniak- og lugtreducerende tiltag i svinestalde og kan være stillet som et vilkår i husdyrbrugets miljøgodkendelse. <br><br>Hvis der er installeret luftrensning, skal man være opmærksom på vilkår om logbog, og hvad der skal registreres heri. Se miljøgodkendelsen.  <br><br>Vedligeholdelse af anlægget skal følge producentens anvisninger.<br>|<br>Used as an ammonia and odor-reducing measure in pig stables and can be made a condition in the livestock farm's environmental approval.<br><br>If air purification is installed, you must be aware of the terms of the logbook and what must be registered in it. See the environmental approval.<br><br>Maintenance of the system must follow the manufacturer's instructions.<br>]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Date"">
					<Id>375177</Id>
					<Label>Sæt startdato for driftsstop|Set startdate for shutdown</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<MinValue>2021-09-08</MinValue>
					<MaxValue>2031-09-08</MaxValue>
					<Value/>
					<Mandatory>false</Mandatory>
					<ReadOnly>false</ReadOnly>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Date"">
					<Id>375178</Id>
					<Label>Sæt slutdato for driftsstop|Set end date for shutdown</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<MinValue>2021-09-08</MinValue>
					<MaxValue>2031-09-08</MaxValue>
					<Value/>
					<Mandatory>false</Mandatory>
					<ReadOnly>false</ReadOnly>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Number"">
					<Id>375174</Id>
					<Label>Driftsstop timer|Downtime hours</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375179</Id>
					<Label>Årsag til driftsstop|Cause of shutdown</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>375172</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375171</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>375173</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142237</Id>
	<Repeated>0</Repeated>
	<Label>08. Luftrensning serviceaftale|08. Air cleaning service agreement</Label>
	<StartDate>2020-12-28</StartDate>
	<EndDate>2030-12-28</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142237</Id>
			<Label>08. Luftrensning serviceaftale|08. Air cleaning service agreement</Label>
			<Description>
				<![CDATA[Billede af serviceaftale/faktura|Picture service agreement/invoice]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372361</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372362</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Når du tager et billede af din serviceftale og/eller faktura, er den nem at finde frem på web, når du har brug for at kunne fremvise denne til f.eks. tilsynsmyndighederne.|When you take a picture of your service agreement and / or invoice, it is easy to find on the web when you need to be able to present it to e.g. supervisory authorities.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Picture"">
					<Id>372360</Id>
					<Label>Billede serviceaftale|Picture service agreement</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372358</Id>
					<Label>Billede faktura|Picture invoice</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372357</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372359</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142236</Id>
	<Repeated>0</Repeated>
	<Label>08. Luftrensning timer|08. Air cleaning hours</Label>
	<StartDate>2020-12-28</StartDate>
	<EndDate>2030-12-28</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142236</Id>
			<Label>08. Luftrensning timer|08. Air cleaning hours</Label>
			<Description>
				<![CDATA[Luftrensning timeaflæsning|Air cleaning hour meter]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372355</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372356</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Bruges som ammoniak- og lugtreducerende tiltag i svinestalde og kan være stillet som et vilkår i husdyrbrugets miljøgodkendelse. <br><br>Hvis der er installeret luftrensning, skal man være opmærksom på vilkår om logbog, og hvad der skal registreres heri. Se miljøgodkendelsen.  <br><br>Vedligeholdelse af anlægget skal følge producentens anvisninger.<br>|<br>Used as an ammonia and odor-reducing measure in pig stables and can be made a condition in the livestock farm's environmental approval.<br><br>If air purification is installed, you must be aware of the terms of the logbook and what must be registered in it. See the environmental approval.<br><br>Maintenance of the system must follow the manufacturer's instructions.<br>]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Number"">
					<Id>372354</Id>
					<Label>Luftrensning timeaflæsning|Air cleaning hour meter</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372352</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372351</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372353</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142590</Id>
	<Repeated>0</Repeated>
	<Label>09. Forsuring driftsstop|09. Acidification downtime</Label>
	<StartDate>2021-09-08</StartDate>
	<EndDate>2031-09-08</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142590</Id>
			<Label>09. Forsuring driftsstop|09. Acidification downtime</Label>
			<Description>
				<![CDATA[Logbog driftsstop|Logbook downtime]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>375184</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>375185</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Bruges som ammoniakreducerende tiltag.<br><br>Hvis der er installeret forsuringsanlæg, skal man være opmærksom på vilkår om logbog, og hvad der skal registreres heri. Se miljøgodkendelsen.<br><br>Vedligeholdelse af anlægget skal følge producentens anvisninger.<br>|<br>Used as an ammonia-reducing measure.<br><br>If acidification systems have been installed, you must be aware of the terms of the logbook and what must be registered in it. See the environmental approval.<br><br>Maintenance of the system must follow the manufacturer's instructions.<br>]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Date"">
					<Id>375186</Id>
					<Label>Sæt startdato for driftsstop|Set startdate for shutdown</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<MinValue>2021-09-08</MinValue>
					<MaxValue>2031-09-08</MaxValue>
					<Value/>
					<Mandatory>false</Mandatory>
					<ReadOnly>false</ReadOnly>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Date"">
					<Id>375187</Id>
					<Label>Sæt slutdato for driftsstop|Set end date for shutdown</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<MinValue>2021-09-08</MinValue>
					<MaxValue>2031-09-08</MaxValue>
					<Value/>
					<Mandatory>false</Mandatory>
					<ReadOnly>false</ReadOnly>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Number"">
					<Id>375183</Id>
					<Label>Driftsstop timer|Downtime hours</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375188</Id>
					<Label>Årsag til driftsstop|Cause of shutdown</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>375181</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375180</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>375182</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142256</Id>
	<Repeated>0</Repeated>
	<Label>09. Forsuring pH værdi|09. Acidification pH value</Label>
	<StartDate>2021-01-13</StartDate>
	<EndDate>2031-01-13</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142256</Id>
			<Label>09. Forsuring pH værdi|09. Acidification pH value</Label>
			<Description>
				<![CDATA[Indtast pH værdi|Enter pH value]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372478</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372479</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Bruges som ammoniakreducerende tiltag.<br><br>Hvis der er installeret forsuringsanlæg, skal man være opmærksom på vilkår om logbog, og hvad der skal registreres heri. Se miljøgodkendelsen.<br><br>Vedligeholdelse af anlægget skal følge producentens anvisninger.<br>|<br>Used as an ammonia-reducing measure.<br><br>If acidification systems have been installed, you must be aware of the terms of the logbook and what must be registered in it. See the environmental approval.<br><br>Maintenance of the system must follow the manufacturer's instructions.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Number"">
					<Id>372477</Id>
					<Label>Gennemsnitlig pH-værdi i gyllen før svovlsyrebehandling|Average pH in slurry before sulfuric acid treatment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372475</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372474</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372476</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142252</Id>
	<Repeated>0</Repeated>
	<Label>09. Forsuring serviceaftale|09. Acidification service agreement</Label>
	<StartDate>2021-01-13</StartDate>
	<EndDate>2031-01-13</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142252</Id>
			<Label>09. Forsuring serviceaftale|09. Acidification service agreement</Label>
			<Description>
				<![CDATA[Billede af serviceaftale/faktura|Picture service agreement/invoice]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372463</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372464</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Forsuringsanlæg skal vedligeholdes i henhold til leverandørens anvisninger, så der kan opnås de i vilkårene angivne ydelser.<br><br>Når du tager et billede af din serviceftale og/eller faktura, er den nem at finde frem på web, når du har brug for at kunne fremvise denne til f.eks. tilsynsmyndighederne.<br>|<br>Acidification systems must be maintained in accordance with the supplier's instructions so that the services specified in the terms can be obtained.<br><br>When you take a picture of your service agreement and / or invoice, it is easy to find on the web when you need to be able to present it to e.g. supervisory authorities.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Picture"">
					<Id>372462</Id>
					<Label>Billede serviceaftale|Picture service agreement</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372460</Id>
					<Label>Billede faktura|Picture invoice</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372459</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372461</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142592</Id>
	<Repeated>0</Repeated>
	<Label>10. Varmepumpe driftsstop|10. Heat pump downtime</Label>
	<StartDate>2021-09-08</StartDate>
	<EndDate>2031-09-08</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142592</Id>
			<Label>10. Varmepumpe driftsstop|10. Heat pump downtime</Label>
			<Description>
				<![CDATA[Logbog driftsstop|Logbook downtime]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>375199</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>375200</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Bruges som ammoniak- og lugtreducerende tiltag i svinestalde (søer, smågrise og slagtesvin) med rørudslusning eller linespil.<br><br>Du skal være opmærksom på, om varmepumpen er med timetæller, energimåler eller en datalogger.<br><br>Hvis varmepumpen har en timetæller og/eller energimåler, bør der registreres årligt timeforbrug/energimåling for at kunne dokumentere, at varmepumpen kører jævnt.<br><br>Hvis der opstår fejl på anlægget, vil der komme en alarm på anlægget. Her skal du følge producentens anvisninger.<br><br>I miljøgodkendelsen kan der være en række vilkår til egenkontrol i forhold til den installerede miljøteknologi.<br>|<br>Used as an ammonia and odor-reducing measure in pig stables (sows, piglets and fattening pigs) with pipe release or line winch.<br><br>You must be aware of whether the heat pump has an hour meter, energy meter or a data logger.<br><br>If the heat pump has an hour meter and or energy meter, an annual consumption should be registered in order to be able to document that the heat pump runs smoothly.<br><br>If a fault occurs in the system, an alarm will sound on the system. Here you must follow the manufacturer's instructions.<br><br>In the environmental approval, there may be a number of conditions for self-inspection in relation to the installed environmental technology.<br>]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Date"">
					<Id>375201</Id>
					<Label>Sæt startdato for driftsstop|Set startdate for shutdown</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<MinValue>2021-09-08</MinValue>
					<MaxValue>2031-09-08</MaxValue>
					<Value/>
					<Mandatory>false</Mandatory>
					<ReadOnly>false</ReadOnly>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Date"">
					<Id>375202</Id>
					<Label>Sæt slutdato for driftsstop|Set end date for shutdown</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<MinValue>2021-09-08</MinValue>
					<MaxValue>2031-09-08</MaxValue>
					<Value/>
					<Mandatory>false</Mandatory>
					<ReadOnly>false</ReadOnly>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Number"">
					<Id>375198</Id>
					<Label>Driftsstop timer|Downtime hours</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375203</Id>
					<Label>Årsag til driftsstop|Cause of shutdown</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>375196</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375195</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>375197</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142591</Id>
	<Repeated>0</Repeated>
	<Label>10. Varmepumpe serviceaftale|10. Heat pump service agreement</Label>
	<StartDate>2021-09-08</StartDate>
	<EndDate>2031-09-08</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142591</Id>
			<Label>10. Varmepumpe serviceaftale|10. Heat pump service agreement</Label>
			<Description>
				<![CDATA[Billede af serviceaftale/faktura|Picture service agreement/invoice]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>375193</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>375194</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Når du tager et billede af din serviceftale og/eller faktura, er den nem at finde frem på web, når du har brug for at kunne fremvise denne til f.eks. tilsynsmyndighederne.<br>|<br>When you take a picture of your service agreement and / or invoice, it is easy to find on the web when you need to be able to present it to e.g. supervisory authorities.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Picture"">
					<Id>375192</Id>
					<Label>Billede serviceaftale|Picture service agreement</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>375190</Id>
					<Label>Billede faktura|Picture invoice</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375189</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>375191</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142445</Id>
	<Repeated>0</Repeated>
	<Label>10. Varmepumpe timer og energi|10. Heat pumps hours and energy</Label>
	<StartDate>2021-06-08</StartDate>
	<EndDate>2031-06-08</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142445</Id>
			<Label>10. Varmepumpe timer og energi|10. Heat pumps hours and energy</Label>
			<Description>
				<![CDATA[Timetæller, MWh og tryk|Hour meter, MWh and pressure]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>374287</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>374288</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Bruges som ammoniak- og lugtreducerende tiltag i svinestalde (søer, smågrise og slagtesvin) med rørudslusning eller linespil. <br><br>Du skal være opmærksom på, om varmepumpen er med timetæller, energimåler eller en datalogger. <br><br>Hvis varmepumpen har en timetæller og/eller energimåler, bør der registreres årligt timeforbrug/energimåling for at kunne dokumentere, at varmepumpen kører jævnt. <br><br>Hvis der opstår fejl på anlægget, vil der komme en alarm på anlægget. Her skal du følge producentens anvisninger.  <br><br>I miljøgodkendelsen kan der være en række vilkår til egenkontrol i forhold til den installerede miljøteknologi.<br>|<br>Used as an ammonia and odor-reducing measure in pig stables (sows, piglets and fattening pigs) with pipe release or line winch.<br><br>You must be aware of whether the heat pump has an hour meter, energy meter or a data logger.<br><br>If the heat pump has an hour meter and or energy meter, an annual consumption should be registered in order to be able to document that the heat pump runs smoothly.<br><br>If a fault occurs in the system, an alarm will sound on the system. Here you must follow the manufacturer's instructions.<br><br>In the environmental approval, there may be a number of conditions for self-inspection in relation to the installed environmental technology.<br>]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Number"">
					<Id>374286</Id>
					<Label>Timetæller|Hour meter</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Number"">
					<Id>374289</Id>
					<Label>Energimåling (MWh)|Energy measurement (MWh)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>374290</Id>
					<Label>Tjek tryk|Check pressure|Перевірте тиск</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>374284</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>374283</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>374285</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142399</Id>
	<Repeated>0</Repeated>
	<Label>11. Pillefyr|11. Pellet stove</Label>
	<StartDate>2021-05-19</StartDate>
	<EndDate>2031-05-19</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142399</Id>
			<Label>11. Pillefyr|11. Pellet stove</Label>
			<Description>
				<![CDATA[Aske, rengøring og mængde piller|Ash, cleaning and amount of pellets]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>373691</Id>
					<Label>Tjek beholder til aske|Check container for ash</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373692</Id>
					<Label>Pillefyr rengjort (hvis nødvendigt)|Pellet stove cleaned (if necessary)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373693</Id>
					<Label>Tjek silo for piller på lager|Check silo for pellets in stock</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>373686</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>373685</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>373687</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142265</Id>
	<Repeated>0</Repeated>
	<Label>12. Affald og farligt affald|12. Waste and hazardous waste</Label>
	<StartDate>2021-01-17</StartDate>
	<EndDate>2031-01-17</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142265</Id>
			<Label>12. Affald og farligt affald|12. Waste and hazardous waste</Label>
			<Description>
				<![CDATA[Dokumentation af korrekt bortskaffelse|Documentation of proper disposal]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372544</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372545</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Kommunens erhvervsaffaldsregulativ skal følges.&nbsp;<br><br>Det skal til en hver tid kunne dokumenteres, hvordan affald er blevet bortskaffet. Fx i form af kvitteringer fra skrothandler, genbrugsplads eller lignende.<br><br>For farligt affald skal der være særlig opmærksomhed på opbevaringen indtil bortskaffelse, så det sikres, at der ikke sker miljøbelastning.<br>&nbsp;<br>Opbevaringen af døde dyr må ikke give anledning til uhygiejniske forhold eller risiko for forurening af jord, grundvand eller overfladevand.<br>|<br>The municipality's commercial waste regulations must be followed.<br><br>It must always be possible to document how waste has been disposed of. For example in the form of receipts from scrap dealers, recycling sites or the like.<br><br>For hazardous waste, special attention must be paid to storage until disposal to ensure that there is no environmental impact.<br><br>The storage of dead animals must not give rise to unhygienic conditions or the risk of contamination of soil, groundwater or surface water.<br>]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Picture"">
					<Id>372541</Id>
					<Label>Billede kvittering|Picture receipts</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372540</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372542</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142262</Id>
	<Repeated>0</Repeated>
	<Label>12. Dieseltank|12. Diesel tank</Label>
	<StartDate>2021-01-17</StartDate>
	<EndDate>2031-01-17</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142262</Id>
			<Label>12. Dieseltank|12. Diesel tank</Label>
			<Description>
				<![CDATA[Kontrolpunkter dieseltank|Checkpoints diesel tank]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372518</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372519</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Opbevaring af dieselolie skal ske i godkendte tanke. Tankene skal være placeret, så risiko for påkørsel minimeres, og så eventuelt spild kan opsamles eller håndteres uden risiko for afstrømning til det omkringliggende miljø.<br><br>Slanger og tankhaner skal være tætte og alle tanke skal være registreret i BBR. Der skal desuden være tankattest og læsbar mærkat med tanktype og alder.&nbsp;<br><br>Et olieabsorberende materiale, eksempelvis kattegrus, skal til enhver tid være let tilgængeligt i umiddelbar nærhed af alle ejendommens&nbsp; dieseltanke, således at spild omgående og effektivt kan opsamles.<br>|<br>Storage of diesel oil must take place in approved tanks. The tanks must be located so that the risk of collision is minimized, and so that any waste can be collected or handled without risk of run-off to the surrounding environment.<br><br>Hoses and tank valves must be tight and all tanks must be registered in the BBR. There must be a tank certificate and a legible sticker with tank type and age.<br><br>An oil-absorbent material, such as cat litter, must at all times be easily accessible in the immediate vicinity of all the property's diesel tanks, so that spills can be collected immediately and efficiently.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>372517</Id>
					<Label>Tankattest OK|Tank certificate OK</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372520</Id>
					<Label>Ingen spild og utætheder|No spills and leaks</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372521</Id>
					<Label>Slange til diesel OK|Hose for diesel OK</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372522</Id>
					<Label>Adgang til olieabsorberende materiale|Access to oil absorbent material</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372515</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372514</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372516</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142264</Id>
	<Repeated>0</Repeated>
	<Label>12. Kemi|12. Chemistry</Label>
	<StartDate>2021-01-17</StartDate>
	<EndDate>2031-01-17</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142264</Id>
			<Label>12. Kemi|12. Chemistry</Label>
			<Description>
				<![CDATA[Kontrolpunkter kemi|Checkpoints chemistry]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372536</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372537</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Pesticider skal altid opbevares utilgængeligt for uvedkommende - altså bag lås.&nbsp;Pesticider skal være godkendte og forsynet med dansk etikette.<br>&nbsp;<br>Der må ikke være afløb i gulvet, hvor pesticider opbevares.<br><br>Vejledning om værnemidler og korrekt anvendelse skal være tilgængelig. Værnemidler skal være tilgængelige.<br>|<br>Pesticides must always be kept out of the reach of unauthorized persons - ie behind locks. Pesticides must be approved and provided with Danish etiquette.<br><br>There must be no drains in the floor where pesticides are stored.<br><br>Guidance on protective equipment and proper use must be available. Protective equipment must be available.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>373365</Id>
					<Label>Kemitjek|Chemistry check</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372538</Id>
					<Label>Ingen spild og utætte beholdere|No spills and leaky containers</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372539</Id>
					<Label>Værnemidler tilgængelig|Protective equipment available</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372533</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372532</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372534</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142263</Id>
	<Repeated>0</Repeated>
	<Label>12. Motor- og spildolie|12. Motor oil and waste oil</Label>
	<StartDate>2021-01-17</StartDate>
	<EndDate>2031-01-17</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142263</Id>
			<Label>12. Motor- og spildolie|12. Motor oil and waste oil</Label>
			<Description>
				<![CDATA[Kontrolpunkter motor- og spildolie|Checkpoints motor oil and waste oil]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372527</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372528</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Opbevaring af både spildolie og ny motorolie skal ske i dertil egnede beholdere.&nbsp;Alle beholdere skal være placeret på fast gulv uden afløb.&nbsp;<br><br>Beholdere kan være placeret i spildbakke eller lignende, der som minimum kan rumme indholdet af den største beholder.<br><br>Bortskaffelse af spildolie, filtre mm. skal ske efter gældende regler og skal kunne dokumenteres.<br><br>Spildolie ikke må anvendes til smøring etc.<br><br>Vær opmærksom på kommunernes erhvervsaffaldsregulativ i forbindelse med fx bortskaffelseshyppighed mv.&nbsp;<br>|<br>Both waste oil and new engine oil must be stored in suitable containers. All containers must be placed on a solid floor without a drain.<br><br>Containers can be placed in a waste bin or similar, which can at least hold the contents of the largest container.<br><br>Disposal of waste oil, filters etc. must be done according to current rules and must be able to be documented.<br><br>Waste oil must not be used for lubrication etc.<br><br>Pay attention to the municipalities' commercial waste regulations in connection with, for example, disposal frequency, etc.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>372526</Id>
					<Label>Tankattest OK|Tank certificate OK</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372529</Id>
					<Label>Ingen spild og utætheder|No spills and leaks</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>372531</Id>
					<Label>Adgang til olieabsorberende materiale|Access to oil absorbent material</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372524</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372523</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372525</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142196</Id>
	<Repeated>0</Repeated>
	<Label>13. APV Medarbejer|13. WPA Worker</Label>
	<StartDate>2020-12-04</StartDate>
	<EndDate>2030-12-04</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142196</Id>
			<Label>13. APV Medarbejer|13. WPA Worker</Label>
			<Description>
				<![CDATA[Tryk for at udfylde arbejdspladsvurdering (APV)<br>|<br>Press to complete workplace assessment (WPA)<br>]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemList>
				<DataItem type=""SingleSelect"">
					<Id>372091</Id>
					<Label>01. Fald til lavere niveau|01. Fall to lower level</Label>
					<Description>
						<![CDATA[Er der risiko for, at I kan falde ned fra fx stiger, plansiloer, gallerier eller bygningen?<br>|<br>Is there a risk that you may fall from ladders, plan silos, galleries or the building?|<br>Чи є ризик, що ви можете впасти зі сходів, планувальних шахт, галерей або будівлі?]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372092</Id>
					<Label>02. Ulykker med maskiner|02. Accidents with machines</Label>
					<Description>
						<![CDATA[Er der risiko for at komme til skade med de maskiner, I bruger til fx høst, fodertilberedning eller gyllehåndtering?<br>|<br>Is there a risk of injury with the machines you use for eg harvesting, feed preparation or slurry handling?]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372093</Id>
					<Label>03. Akut fysisk overbelastning|03. Acute physical overload</Label>
					<Description>
						<![CDATA[Er der risiko for akut overbelastning af kroppen, når I løfter, driver med dyr, trækker eller skubber fx kalve og&nbsp;grise eller tunge materialer på bedriften?<br>|<br>Is there a risk of acute overload of the body when you lift, drive animals, pull or push eg calves and pigs or heavy materials on the farm?]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372094</Id>
					<Label>04. Fald og snublen|04. Fall and stumble</Label>
					<Description>
						<![CDATA[Er der risiko for, at I kan falde eller snuble over fx rod eller paller i stalden, laden, maskinhuset eller udendørs,&nbsp;eller fordi der er glat på plansiloen, i stalden eller udendørs?<br>|<br>Is there a risk that you may fall or stumble over, for example, clutter or pallets in the barn, barn, machine house or outdoors, or because it is slippery on the flat silo, in the barn or outdoors?]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372095</Id>
					<Label>05. Ulykker med håndværktøj og håndmaskiner|05. Accidents with hand tools and hand machines</Label>
					<Description>
						<![CDATA[Er der risiko for at skære sig eller at få fingrene i klemme, når I arbejder med håndværktøj som fx kanyler,&nbsp;boltpistoler, motorsave, vinkelslibere og boremaskiner?<br>|<br>Is there a risk of cutting yourself or getting your fingers pinched when working with hand tools such as needles, bolt guns, chainsaws, angle grinders and drills?]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372096</Id>
					<Label>06. Intern færdsel|06. Internal traffic</Label>
					<Description>
						<![CDATA[Er der risiko for, at I kan blive påkørt eller klemt af fx traktorer, knækstyrede frontlæssere og fladvogne, når I&nbsp;kører på ejendommen?<br>|<br>Is there a risk that you may be hit or crushed by eg tractors, articulated front loaders and flatbed trucks when driving on the property?]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372097</Id>
					<Label>07. Dårlige arbejdsstillinger|07. Poor working positions</Label>
					<Description>
						<![CDATA[Arbejder I med foroverbøjet ryg, løftede arme, på hug eller i andre dårlige arbejdsstillinger, eller står og går I det&nbsp;meste af arbejdsdagen?<br>|<br>Do you work with your back bent, your arms raised, squatting or in other bad working positions, or do you stand and walk most of the working day?]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372098</Id>
					<Label>08. Ensidigt, belastende arbejde|08. One-sided, stressful work</Label>
					<Description>
						<![CDATA[Belaster I kroppen på samme måde over længere tid – fx når I udfører pløjning med drejet nakke, kastrerer&nbsp;mange smågrise, eller aftørrer og afrenser yvere?<br>|<br>Do you strain your body in the same way for a long time - for example when you plow with a twisted neck, castrate many piglets, or wipe and clean udders?]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372099</Id>
					<Label>09. Tunge løft|09. Heavy lifting</Label>
					<Description>
						<![CDATA[Løfter I sække med foder, maskindele, kalve eller andre tunge emner på bedriften?<br>|<br>Do you lift sacks of feed, machine parts, calves or other heavy items on the farm?]]>
					</Description>
					<DisplayOrder>8</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372100</Id>
					<Label>10. Træk og skub|10. Pull and push</Label>
					<Description>
						<![CDATA[Bruger I mange kræfter, når I skal trække eller skubbe fx tunge trillebøre, fodervogne, palleløftere eller&nbsp;kadavervogne?<br>|<br>Do you use a lot of force when you have to pull or push, for example, heavy wheelbarrows, feed carts, pallet trucks or carcass carts?]]>
					</Description>
					<DisplayOrder>9</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372101</Id>
					<Label>11. Høj støj|11. Loud noise</Label>
					<Description>
						<![CDATA[Arbejder I med vinkelslibere, motorsave og højtryksrensere eller andre meget støjende maskiner?<br>|<br>Do you work with angle grinders, chainsaws and high pressure cleaners or other very noisy machines?]]>
					</Description>
					<DisplayOrder>10</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372102</Id>
					<Label>12. Stor arbejdsmængde, tidspres og uklare krav|12. Large workload, time pressure and unclear requirements</Label>
					<Description>
						<![CDATA[Har I ofte for mange opgaver eller for travlt i bedriften?<br>|<br>Do you often have too many tasks or too busy on the farm?]]>
					</Description>
					<DisplayOrder>11</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372103</Id>
					<Label>13. Hjælp og støtte fra ledelse og kolleger|13. Help and support from management and colleagues</Label>
					<Description>
						<![CDATA[Mangler I hjælp og støtte fra jeres ledelse og kolleger?<br>|<br>Do you lack help and support from your management and colleagues?]]>
					</Description>
					<DisplayOrder>12</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372104</Id>
					<Label>14. Mobning|14. Bullying</Label>
					<Description>
						<![CDATA[Er der nogen på arbejdspladsen, der bliver udsat for mobning?<br>|<br>Is anyone in the workplace being bullied?]]>
					</Description>
					<DisplayOrder>13</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372105</Id>
					<Label>15. Infektionsrisiko|15. Risk of infection</Label>
					<Description>
						<![CDATA[Er der risiko for infektioner eller luftvejsbelastninger, når I arbejder med dyr?<br>|<br>Is there a risk of infections or respiratory loads when working with animals?]]>
					</Description>
					<DisplayOrder>14</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372106</Id>
					<Label>16. Farlige stoffer og materialer|16. Hazardous substances and materials</Label>
					<Description>
						<![CDATA[Arbejder I med faremærkede produkter som fx bekæmpelsesmidler, desinficeringsmidler, rengøringsmidler og&nbsp;flydende ammoniak eller med andre produkter som fx gødning, antibiotika og andre veterinærlægemidler, der&nbsp;kan indeholde farlige stoffer og materialer?<br>|<br>Do you work with hazardous products such as pesticides, disinfectants, cleaning agents and liquid ammonia or with other products such as fertilizers, antibiotics and other veterinary medicines that may contain dangerous substances and materials?]]>
					</Description>
					<DisplayOrder>15</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372107</Id>
					<Label>17. Støv, gasser og røg|17. Dust, gases and fumes</Label>
					<Description>
						<![CDATA[Er der risiko for, at I bliver udsat for støv, gas eller røg fra flis, hø, halm, korn, gylle, fisk der rådner, ensilage,&nbsp;udstødning eller svejsning?<br>|<br>Is there a risk that you will be exposed to dust, gas or smoke from wood chips, hay, straw, grain, manure, rotting fish, silage, exhaust or welding?]]>
					</Description>
					<DisplayOrder>16</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372108</Id>
					<Label>18. Våde eller fugtige hænder|18. Wet or damp hands</Label>
					<Description>
						<![CDATA[Arbejder I med våde eller fugtige hænder i mere end 2 timer om dagen?<br>|<br>Do you work with wet or damp hands for more than 2 hours a day?]]>
					</Description>
					<DisplayOrder>17</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372109</Id>
					<Label>19. Helkropsvibrationer|19. Whole body vibrations</Label>
					<Description>
						<![CDATA[Kører I med traktorer, høstmateriel, minilæssere og andre maskiner, der udsætter jer for kraftige vibrationer?<br>|<br>Do you drive with tractors, harvesting equipment, mini loaders and other machines that expose you to strong vibrations?]]>
					</Description>
					<DisplayOrder>18</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372110</Id>
					<Label>20. Hånd-armvibrationer|20. Hand-arm vibrations</Label>
					<Description>
						<![CDATA[Har I snurrende eller følelsesløse fingre, når I har arbejdet med meget vibrerende værktøj som fx motorsave,&nbsp;højtryksrensere og vinkelslibere?<br>|<br>Do you have tingling or numb fingers when you have worked with very vibrating tools such as chainsaws, high-pressure cleaners and angle grinders?]]>
					</Description>
					<DisplayOrder>19</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>372112</Id>
					<Label>21. Alt i alt oplever jeg, at arbejdsforholdende er gode på min arbejdsplads|21. All in all, I find that working conditions are good in my workplace</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>20</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[I høj grad|Very much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[I mindre grad|Not so much]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Slet ikke|Not at all]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>fff6df</Color>
				</DataItem>
				<DataItem type=""Text"">
					<Id>372113</Id>
					<Label>Kommentarer til arbejdsforholdende|Comments on working conditions</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>21</DisplayOrder>
					<Multi>0</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>fff6df</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372111</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>22</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142401</Id>
	<Repeated>0</Repeated>
	<Label>14. Maskiner|14. Machines</Label>
	<StartDate>2021-05-20</StartDate>
	<EndDate>2031-05-20</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142401</Id>
			<Label>14. Maskiner|14. Machines</Label>
			<Description>
				<![CDATA[<br>]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>373705</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>373706</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Det er vigtigt, at maskinparken løbende vedligeholdes for at sikre optimal drift og for at undgå uheld.<br>|<br>It is important that the machinery is continuously maintained to ensure optimal operation and to avoid accidents.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Date"">
					<Id>373704</Id>
					<Label>Indtast dato for sidste service|Enter date of last service</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<MinValue>2021-09-08</MinValue>
					<MaxValue>2031-09-08</MaxValue>
					<Value/>
					<Mandatory>false</Mandatory>
					<ReadOnly>false</ReadOnly>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373707</Id>
					<Label>Hydrauliksystem og slanger kontrolleret|Hydraulic system and hoses checked</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373708</Id>
					<Label>Lys, blinklys og bremser kontrolleret|Lights, turn signals and brakes checked</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373709</Id>
					<Label>Ingen utætheder|No leaks</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373710</Id>
					<Label>Sliddele kontrolleret|Wear parts checked</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373711</Id>
					<Label>Olie kontrolleret|Oil checked</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>373702</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>373701</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>8</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>373703</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>9</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142593</Id>
	<Repeated>0</Repeated>
	<Label>15. Elværktøj|15. Power tools</Label>
	<StartDate>2021-09-08</StartDate>
	<EndDate>2031-09-08</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142593</Id>
			<Label>15. Elværktøj|15. Power tools</Label>
			<Description>
				<![CDATA[]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>375208</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>375209</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Tilpas evt. tjeklisten i <b><i>Microting eForm Editor</i></b>, med baggrund i anvisninger for eftersyn af det aktuelle redskab og i henhold til de informationer leverandøren har anført i håndværktøjets brugsanvisning.<br>|<br>Adjust - if necessary - the checklist in the <b><i>Microting eForm Editor</i></b>, based on instructions for inspecting the current tool and according to the information provided by the supplier in the manual of the hand tool.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>375210</Id>
					<Label>Ledninger OK|Wires OK</Label>
					<Description>
						<![CDATA[Tjek for skader, revner eller huller på ledninger|Check for damage, cracks or holes in wires]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375211</Id>
					<Label>Stik OK|Plug OK</Label>
					<Description>
						<![CDATA[Tjek for skader, revner mm.|Check for damage, cracks etc.]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375207</Id>
					<Label>Værktøjshus og håndtag OK|Tool house and handle OK</Label>
					<Description>
						<![CDATA[Tjek for skader, revner, brud mv.|Check for damage, cracks, fractures, etc.]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375212</Id>
					<Label>Maskine i god stand|Machine in good condition</Label>
					<Description>
						<![CDATA[Tjek om el-materiellet er rent, fri for fedt og har frie ventilationsåbninger| Check that the electrical equipment is clean, free of grease and has free ventilation openings]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375213</Id>
					<Label>Ingen tegn på overbelastning|No signs of congestion</Label>
					<Description>
						<![CDATA[Tjek for synlige spor mv.|Check for visible traces etc.]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375214</Id>
					<Label>Betjeningsknapper OK|Control buttons OK</Label>
					<Description>
						<![CDATA[Tjek at START/STOP knapper og andre kontakter virker efter hensigten|Check that START/STOP buttons and other switches work as intended]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>375215</Id>
					<Label>Kan elværktøjet godkendes?|Can the power tool be approved?</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[Ja|Yes]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[Nej, skal repareres|No, needs to be repaired]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Nej, skal kasseres|No, must be discarded]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>375205</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>8</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375204</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>9</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>375206</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>10</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142594</Id>
	<Repeated>0</Repeated>
	<Label>16. Stiger|16. Ladders</Label>
	<StartDate>2021-09-08</StartDate>
	<EndDate>2031-09-08</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142594</Id>
			<Label>16. Stiger|16. Ladders</Label>
			<Description>
				<![CDATA[]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>375220</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>375221</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Tilpas evt. tjeklisten i <b><i>Microting eForm Editor</i></b>, med baggrund i anvisninger for eftersyn af den aktuelle stige og i henhold til de informationer leverandøren har anført i stigens brugsanvisning.<br>|<br>Adjust - if necessary - the checklist in the <b><i>Microting eForm Editor</i></b>, based on instructions for inspecting the current ladder and according to the information provided by the supplier in the manual of the ladder.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>375228</Id>
					<Label>Stige udført i henhold til Standard EN 131|Ladder made in accordance with Standard EN 131</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375229</Id>
					<Label>Anden standard|Other standard</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375222</Id>
					<Label>Stigebeslag OK|Ladder fittings OK</Label>
					<Description>
						<![CDATA[Ikke skæve, løse, mv.|Not crooked, loose, etc.]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375223</Id>
					<Label>Kæder OK|Chains OK</Label>
					<Description>
						<![CDATA[Ingen løs befæstning, ikke itu, mv.|No loose fastening, not broken, etc.]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375219</Id>
					<Label>Vanger OK|Stringer OK</Label>
					<Description>
						<![CDATA[Ikke skæve, flækkede, mv.|Not crooked, cracked, etc.]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375224</Id>
					<Label>Trin OK|Steps OK</Label>
					<Description>
						<![CDATA[Ikke skæve, løse, mv.| Not crooked, loose, etc.]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375225</Id>
					<Label>Stigedupper OK|Ladder pads OK</Label>
					<Description>
						<![CDATA[Ikke skæve, revnede, mv.|Not crooked, cracked, etc.]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375226</Id>
					<Label>Efterbehandling OK|Subsequent treatment OK</Label>
					<Description>
						<![CDATA[F.eks. ingen skader på lakering, mv.| Eg. no damage to paintwork, etc.]]>
					</Description>
					<DisplayOrder>8</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375230</Id>
					<Label>Ingen tegn på misbrug|No signs of abuse</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>9</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375231</Id>
					<Label>Ikke vakkelvorn ved brug|Do not wobble when used</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>10</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>375227</Id>
					<Label>Kan stigen godkendes?|Can the ladder be approved?</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>11</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[Ja|Yes]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[Nej, skal repareres|No, needs to be repaired]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Nej, skal kasseres|No, must be discarded]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>375217</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>12</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375216</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>13</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>375218</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>14</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142595</Id>
	<Repeated>0</Repeated>
	<Label>17. Håndildslukkere|17. Fire extinguishers</Label>
	<StartDate>2021-09-08</StartDate>
	<EndDate>2031-09-08</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142595</Id>
			<Label>17. Håndildslukkere|17. Fire extinguishers</Label>
			<Description>
				<![CDATA[]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>375236</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>375237</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Tilpas evt. tjeklisten i <b><i>Microting eForm Editor</i></b>, med baggrund i anvisninger for eftersyn af den aktuelle ildslukker og i henhold til de informationer leverandøren har anført i ildslukkerens brugsanvisning.<br>|<br>Adjust - if necessary - the checklist in the <b><i>Microting eForm Editor</i></b>, based on instructions for inspecting the current fire extinguisher and according to the information provided by the supplier in the manual of the fire extinguisher.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>375244</Id>
					<Label>Ildslukker udført i henhold til DS/EN 3|Fire extinguisher made in accordance with DS/EN 3</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375245</Id>
					<Label>Anden standard|Other standard</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375238</Id>
					<Label>Ophængning OK|Suspension OK</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375239</Id>
					<Label>Tilgængelighed OK|Availability OK</Label>
					<Description>
						<![CDATA[Tjek, at der ikke er placeret gods mv. foran håndslukkeren| Check that no goods, etc. have been placed in front of the fire extinguisher]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375235</Id>
					<Label>Sikkerhedsskiltning OK|Safety signs OK</Label>
					<Description>
						<![CDATA[Tjek, at skilte er opsat på væg eller lignende|Check that signs are mounted on the wall or similar]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375240</Id>
					<Label>Funktionsklar og intakt|Functional and intact</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375241</Id>
					<Label>Brugsanvisning på slukker OK|Instructions for use on extinguisher OK</Label>
					<Description>
						<![CDATA[Tjek, at informationer vedrørende brug af ildslukker kan læses|Check that information regarding the use of the extinguisher can be read]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375242</Id>
					<Label>Trykmåler OK|Pressure gauge OK</Label>
					<Description>
						<![CDATA[Tjek, at der vises korrekt driftstryk|Check that the correct operating pressure is displayed]]>
					</Description>
					<DisplayOrder>8</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375246</Id>
					<Label>Ingen synlige beskadigelser|No visible damages</Label>
					<Description>
						<![CDATA[Tjek beholder og håndtag|Check container and handle]]>
					</Description>
					<DisplayOrder>9</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375247</Id>
					<Label>Kontrolvejning af CO2-slukker OK|Control weighing of CO2 extinguisher</Label>
					<Description>
						<![CDATA[Se beholder for vægtangivelse|See container for weight declaration]]>
					</Description>
					<DisplayOrder>10</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>375248</Id>
					<Label>Plomberingen OK|The seal is OK</Label>
					<Description>
						<![CDATA[Tjek om plomberingen er intakt|Check if seal is intact]]>
					</Description>
					<DisplayOrder>11</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SingleSelect"">
					<Id>375243</Id>
					<Label>Kan ildslukkeren godkendes?|Can the fire extinguisher be approved?</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>12</DisplayOrder>
					<Mandatory>false</Mandatory>
					<KeyValuePairList>
						<KeyValuePair>
							<Key>1</Key>
							<Value>
								<![CDATA[Ja|Yes]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>1</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>2</Key>
							<Value>
								<![CDATA[Nej, skal repareres|No, needs to be repaired]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>2</DisplayOrder>
						</KeyValuePair>
						<KeyValuePair>
							<Key>3</Key>
							<Value>
								<![CDATA[Nej, skal kasseres|No, must be discarded]]>
							</Value>
							<Selected>false</Selected>
							<DisplayOrder>3</DisplayOrder>
						</KeyValuePair>
					</KeyValuePairList>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>375233</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>13</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>375232</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>14</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>375234</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>15</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142348</Id>
	<Repeated>0</Repeated>
	<Label>18. Alarm|18. Alarm</Label>
	<StartDate>2021-03-24</StartDate>
	<EndDate>2031-03-24</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142348</Id>
			<Label>18. Alarm|18. Alarm</Label>
			<Description>
				<![CDATA[Tjek af alarm<br>|<br>Alarm check]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>373209</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>373210</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Tjek om der er forbindelse til netværk og test, at alarmen virker<br>|<br>Check if there is a connection to the network and test that the alarm works]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>373211</Id>
					<Label>Forbindelse til netværk OK|Network connection OK</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373212</Id>
					<Label>Test af alarm OK|Alarm test OK</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>373207</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>373206</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>373208</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142425</Id>
	<Repeated>0</Repeated>
	<Label>19. Ventilation|19. Ventilation</Label>
	<StartDate>2021-05-27</StartDate>
	<EndDate>2031-05-27</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142425</Id>
			<Label>19. Ventilation|19. Ventilation</Label>
			<Description>
				<![CDATA[Tjek ventilation<br>|<br>Check ventilation]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>373948</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>373949</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Et velfungerende ventilationsanlæg medvirker til at minimere sygdomme og øge dyrevelfærden.<br>|<br>A well-functioning ventilation system helps to minimize diseases and increase animal welfare.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>373950</Id>
					<Label>Nødopluk OK|Emergency opening OK</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>373946</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>373945</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>373947</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142352</Id>
	<Repeated>0</Repeated>
	<Label>20. Arbejdsopgave udført|20. Task completed</Label>
	<StartDate>2021-04-05</StartDate>
	<EndDate>2031-04-05</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142352</Id>
			<Label>20. Arbejdsopgave udført|20. Task completed</Label>
			<Description>
				<![CDATA[]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>373242</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>373243</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Når du har udført opgaven, skal du sætte kryds i <b>Opgave udført</b> og derefter trykke på <b>GEM</b>. Du kan også tage billeder og skrive en kommentar til opgaven, før du gemmer.<br>|<br>When you have completed the task, check<b> Task completed</b> and then press <b>SAVE</b>. You can also take pictures and write a comment on the task before saving.</b>, а потім натисніть<b>зберегти < /b>. Ви також можете зробити знімки і написати коментар до завдання перед збереженням.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>373244</Id>
					<Label>Opgave udført|Task completed</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>11</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>373240</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>12</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>373239</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>13</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>373241</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>14</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142381</Id>
	<Repeated>0</Repeated>
	<Label>21. DANISH Produktstandard v_1_01|21. DANISH Product standard v_1_01</Label>
	<StartDate>2021-05-07</StartDate>
	<EndDate>2031-05-07</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142381</Id>
			<Label>21. DANISH Produktstandard v_1_01|21. DANISH Product standard v_1_01</Label>
			<Description>
				<![CDATA[Egenkontrolprogram for besætninger, der er certificeret under DANISH Produktstandard.<br>|<br>Self-inspection program for herds certified under DANISH Product Standard.]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>373486</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>373487</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Egenkontrolprogrammet, der er en del af DANISH Produkt-standard, skal som minimum kontrolleres én gang årligt, og underskrives af den ansvarlige for besætningen, hvilket vil blive kontrolleret ved DANISH-besøg. Som vejledning til punkterne vedrørende dyrevelfærd, henvises til ""Vejledning om god dyrevelfærd i besætninger med grise"".<br>|<br>The self-inspection program, which is part of the DANISH Product Standard, must be inspected at least once a year and signed by the person in charge of the crew, which will be inspected during DANISH visits. For guidance on animal welfare items, please refer to ""Vejledning om god dyrevelfærd i besætninger med grise"".]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""CheckBox"">
					<Id>373488</Id>
					<Label>Branchekode for god produktionspraksis i primærproduktionen er udfyldt og underskrevet.|Industry code for good production practice in primary production is filled in and signed.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373490</Id>
					<Label>Der er dokumentation for alle udførte dyrlægebesøg. Besøgsrapporterne gemmes i to år.|There is documentation for all performed veterinary visits. The visit reports are stored for two years.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373491</Id>
					<Label>Hvis der er indgået en sundheds-rådgivningsaftale, kan denne fremvises, og er der udfærdiget  handlingsplaner i relation til målrettet dyrevelfærdsindsats, kan disse også fremvises.|If a health advisory agreement has been entered into, this can be presented, and if action plans have been drawn up in relation to targeted animal welfare efforts, these can also be presented.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373492</Id>
					<Label>Alle dyr tilses mindst en gang dagligt.|All animals are inspected at least once a day.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""None"">
					<Id>373493</Id>
					<Label>Her kontrolleres også at: |Here it is also checked that:</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373494</Id>
					<Label>a. Alle dyr har vand og foder.|a. All animals have water and food.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373495</Id>
					<Label>b. Arealkravene er overholdt.|b. The area requirements have been complied with.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373496</Id>
					<Label>c. Alle dyr kan rejse, lægge og hvile sig uden besvær.|c. All animals can get up, lie down and rest without difficulty.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>8</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373497</Id>
					<Label>d. Gulvene ikke er glatte eller ujævne.|d. The floors are not slippery or uneven.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>9</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373498</Id>
					<Label>Alt automatiseret eller mekanisk udstyr efterses mindst én gang om dagen.|All automated or mechanical equipment is inspected at least once a day.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>10</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373499</Id>
					<Label>Det sikres, at alle indgreb (kastration, halekupering, tandslibning og jernbehandling) foretages efter lovgivningen, og at der udvises fornøden omhu og hygiejne.|It is ensured that all procedures (castration, tail docking, tooth grinding and iron treatment) are carried out in accordance with the law, and that the necessary care and hygiene is exercised.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>11</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373500</Id>
					<Label>Fra 1. januar 2019 er alle halebid registreret.|From 1 January 2019, all tail bites are registered.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>12</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373501</Id>
					<Label>Fra 1. april 2019 foreligger der en opdateret risikovurdering og handlingsplan.|From 1 April 2019, there will be an updated risk assessment and action plan.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>13</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373502</Id>
					<Label>Fra 1. juli 2019 er der ved salg af halekuperede smågrise, både i Danmark og udland, indhentet dokumentation fra modtageren eller mellemhandleren om, at der er behov for at modtage halekuperede grise.|From 1 July 2019, when selling tail docking piglets, both in Denmark and abroad, documentation has been obtained from the recipient or middleman that there is a need to receive tail docking pigs.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>14</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373503</Id>
					<Label>Der foreligger en målsætning med initiativer, der øger pattegrise-overlevelsen. Denne målsætning følges op årligt.|There is an objective with initiatives that increase piglet survival. This objective is followed up annually.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>15</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373504</Id>
					<Label>Syge, tilskadekomne og aggressive dyr bliver om nødvendigt isoleret, evt. flyttet til sygesti og evt. behandlet. Det sikres, at der er nok sygestier, og at disse er indrettet korrekt. Det sikres, at der føres ekstra tilsyn ved sammenblanding af dyr, for hurtigt at kunne gribe ind.|Sick, injured and aggressive animals are isolated if necessary moved to sick pen and treatment. It is ensured that there are enough sick pens and that these are arranged correctly. It is ensured that extra supervision is carried out when mixing animals, in order to be able to intervene quickly.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>16</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373505</Id>
					<Label>Ved det daglige tilsyn kontrolleres, om der er tilfælde af halebid og/eller skuldersår. Eventuelle nye tilfælde sættes i behandling.|During the daily inspection, check whether there are cases of tail bites and / or shoulder ulcers. Any new cases are put into treatment.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>17</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373506</Id>
					<Label>Der forefindes velfungerende boltpistol samt en skarp kniv til afblødning og/eller udstyr til rygmarvsstødning.|There is a well-functioning bolt gun as well as a sharp knife for de-bleeding and / or equipment for spinal cord impact.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>18</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373507</Id>
					<Label>Det overvåges, at alle grise har permanent adgang til en tilstrækkelig mængde halm eller andet manipulerbart materiale, der kan opfylde deres behov for beskæftigelses- og rodemateriale.|It is monitored that all pigs have permanent access to a sufficient amount of straw or other manipulable material that can meet their need for recreational material.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>19</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e2f4fb</Color>
				</DataItem>
				<DataItem type=""CheckBox"">
					<Id>373489</Id>
					<Label>Det sikres, at det kun er transportegnede grise, der flyttes til udleverings-faciliteterne (udleveringsrummet). I tvivlstilfælde flyttes grisene til en særskilt sti, hvor vognmanden, eventuelt en dyrlæge, vurderer dyrenes transportegnethed. Der skal foreligge dokumentation fra dyrlægen om årlig gennemgang af faktaark fra SEGES om transportegnethed.|It is ensured that only transportable pigs are moved to the delivery facilities (delivery room). In case of doubt, the pigs are moved to a separate pen, where the haulier, possibly a veterinarian, assesses the animals' transportability. There must be documentation from the veterinarian about the annual review of fact sheets from SEGES on transport suitability.</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>20</DisplayOrder>
					<Selected>false</Selected>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>373484</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>21</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>373483</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>22</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>373485</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>23</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142212</Id>
	<Repeated>0</Repeated>
	<Label>22. Sigtetest|22. Sieve test</Label>
	<StartDate>2020-12-16</StartDate>
	<EndDate>2030-12-16</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142212</Id>
			<Label>22. Sigtetest|22. Sieve test</Label>
			<Description>
				<![CDATA[Tryk for at udføre sigtetest|Press to perform sieve test<br>]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372712</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372713</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Foderets partikelfordeling er et vigtigt område, da det påvirker både foderudnyttelsen og mave-/tarm-sundheden.<br><br>Indtast %. Skal summe til 100 %.<br>|<br>The feed particle distribution is an important area, as it affects both feed conversion and stomach / intestinal health.<br><br>Enter %. Must sum to 100 %.]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Number"">
					<Id>372193</Id>
					<Label>Under 1 mm (%)|Less than 1 mm (%)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Number"">
					<Id>372194</Id>
					<Label>1-2 mm (%)|1-2 mm (%)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Number"">
					<Id>372195</Id>
					<Label>Over 2 mm (%)|More than 2 mm (%)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372196</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Text"">
					<Id>372197</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Multi>0</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372192</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>7</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Main>
	<Id>142243</Id>
	<Repeated>0</Repeated>
	<Label>25. Markvandingsforbrug|25. Field irrigation consumption</Label>
	<StartDate>2021-01-11</StartDate>
	<EndDate>2031-01-11</EndDate>
	<Language>da</Language>
	<MultiApproval>false</MultiApproval>
	<FastNavigation>false</FastNavigation>
	<Review>false</Review>
	<Summary>false</Summary>
	<DisplayOrder>0</DisplayOrder>
	<ElementList>
		<Element type=""DataElement"">
			<Id>142243</Id>
			<Label>25. Markvandingsforbrug|25. Field irrigation consumption</Label>
			<Description>
				<![CDATA[Aflæs forbrug|Read consumption]]>
			</Description>
			<DisplayOrder>0</DisplayOrder>
			<ReviewEnabled>false</ReviewEnabled>
			<ManualSync>false</ManualSync>
			<ExtraFieldsEnabled>false</ExtraFieldsEnabled>
			<DoneButtonDisabled>true</DoneButtonDisabled>
			<ApprovalEnabled>false</ApprovalEnabled>
			<DataItemGroupList>
				<DataItemGroup type=""FieldGroup"">
					<Id>372400</Id>
					<Label>LÆS MERE|READ MORE</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>0</DisplayOrder>
					<Value>Closed</Value>
					<Color>fff6df</Color>
					<DataItemList>
						<DataItem type=""None"">
							<Id>372401</Id>
							<Label>INFO|INFO</Label>
							<Description>
								<![CDATA[Aflæs Timetæller&nbsp;(timer) eller elmåler&nbsp;(kWh) eller vandmåler&nbsp;(m3)<br>|<br>Read Hour meter (hours) or electricity meter (kWh) or water meter (m3)]]>
							</Description>
							<DisplayOrder>0</DisplayOrder>
							<Color>fff6df</Color>
						</DataItem>
					</DataItemList>
				</DataItemGroup>
			</DataItemGroupList>
			<DataItemList>
				<DataItem type=""Number"">
					<Id>372402</Id>
					<Label>Timetæller (timer)|Hour meter (hours)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>1</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Number"">
					<Id>372403</Id>
					<Label>Elmåler (kWh)|Electricity meter (kWh)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>2</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Number"">
					<Id>372399</Id>
					<Label>Vandmåler (m3)|Water meter (m3)</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>3</DisplayOrder>
					<Mandatory>false</Mandatory>
					<MinValue/>
					<MaxValue/>
					<Value/>
					<DecimalCount/>
					<UnitName/>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Picture"">
					<Id>372397</Id>
					<Label>Billede|Picture</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>4</DisplayOrder>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""Comment"">
					<Id>372396</Id>
					<Label>Kommentar|Comment</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>5</DisplayOrder>
					<Multi>1</Multi>
					<GeolocationEnabled>false</GeolocationEnabled>
					<Split>false</Split>
					<Value/>
					<ReadOnly>false</ReadOnly>
					<Mandatory>false</Mandatory>
					<Color>e8eaf6</Color>
				</DataItem>
				<DataItem type=""SaveButton"">
					<Id>372398</Id>
					<Label>Gem registrering|Save registration</Label>
					<Description>
						<![CDATA[]]>
					</Description>
					<DisplayOrder>6</DisplayOrder>
					<Value>GEM|SAVE</Value>
					<Color>f0f8db</Color>
				</DataItem>
			</DataItemList>
		</Element>
	</ElementList>
</Main>",
		};
	}
}
