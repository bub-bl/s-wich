# Crowbar

Base runtime reconstruite autour de Silk.NET :

- `Crowbar.Engine` expose les contrats de plateforme et l’implémentation SDL.
- `Crowbar.Editor` est actuellement le bootstrap de l’application et crée la fenêtre via Silk.NET.Windowing.
- Le rendu WebGPU et l’UI seront ajoutés dans des couches séparées.

Pour lancer la fenêtre :

```powershell
dotnet run --project src\Editor\Editor.csproj
```
