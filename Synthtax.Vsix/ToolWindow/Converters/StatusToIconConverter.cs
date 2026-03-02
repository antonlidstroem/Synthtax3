// Finns i SeverityToColorConverter.cs — denna fil re-exporterar inte;
// csproj refererar StatusToIconConverter.cs separat.
// Innehållet ligger i Synthtax.Vsix.ToolWindow.Converters-namnrymden
// via SeverityToColorConverter.cs.

// OBS: Lägg till InverseBoolToVisibilityConverter och StatusToIconConverter
// i SeverityToColorConverter.cs (alla tre klasser i samma namespace-fil)
// — de delas via ett enda Compile Include i .csproj.

// Denna fil är en placeholder för csproj-kompatibilitet om du väljer
// att dela upp konverterarna i separata filer. Innehållet nedan
// duplicerar bara typens deklaration som partial om så önskas.

// För enkelhetens skull: ta bort denna fil och behåll bara
// SeverityToColorConverter.cs som innehåller alla tre konverterare.
