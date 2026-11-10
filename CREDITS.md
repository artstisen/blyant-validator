# Credits

Blyant Validator would not exist without the work of the people behind these open-source projects.
Many thanks to them.

## Modelled on

### The Nu Html Checker (v.Nu)

<https://github.com/validator/validator> · MIT License

The Nu Html Checker is the engine behind the W3C's HTML validator at <https://validator.w3.org/nu/>.
Blyant Validator's native HTML checks are modelled on its behaviour and messages. The optional
`--engine vnu` mode runs the Nu Html Checker itself (`vnu.jar`, downloaded separately) and shows its results.

### W3C CSS Validation Service

<https://github.com/w3c/css-validator> · W3C Software and Document Notice and License

The W3C CSS Validation Service is the reference CSS validator at <https://jigsaw.w3.org/css-validator/>.
Blyant Validator's native CSS checks are modelled on its behaviour and messages.

Blyant Validator is an independent project, written from scratch in C#. It is not affiliated with or endorsed
by the W3C or the authors of the projects above. No source code from either project is included. If any is
added in the future, its licence text will be added to [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Built with

| Library | Used for | Licence |
| --- | --- | --- |
| [AngleSharp](https://github.com/AngleSharp/AngleSharp) | Spec-compliant HTML5 parsing | MIT |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | Command-line parsing | MIT |

Licence texts for these libraries are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
