using System.Security.Claims;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.IntegrationTests;
// The historical repository fixture has no remote service hosts. Policy tests
// exercise the real Configuration store separately; production has no bypass.
internal class CountryPolicyFixtureClient : IServiceClient
{
    public Task<T> SendServiceRequest<T>(
        string service,
        HttpMethod method,
        string path,
        object? body = null,
        ClaimsPrincipal? actor = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        object result;
        if (service == "configuration" && method == HttpMethod.Get && path == "/settings")
        {
            result = new PlatformSettings
            {
                Version = 1,
                OperatingCountry = "ZA",
                Branding = new global::DisputePortal.Platform.Contracts.BrandingDefinition
                {
                    Name = "Dispute Portal",
                    AccentColor = "#005776",
                    LogoColor = "#e7352b"
                },
                Registration = new global::DisputePortal.Platform.Contracts.RegistrationRequirements
                {
                    RequireEmail = false,
                    RequirePhone = false,
                    RequireDocument = false,
                    RequireAddress = false,
                    RequireVerification = false
                },
                Currencies = [new global::DisputePortal.Platform.Contracts.CurrencyDefinition
                {
                    Code = "ZAR",
                    Name = "South African rand",
                    Symbol = "R",
                    Decimals = 2
                }

                ],
                Countries = [new global::DisputePortal.Platform.Contracts.CountryDefinition
                {
                    Code = "ZA",
                    Name = "South Africa",
                    Flag = "",
                    DialCode = "+27",
                    Locale = "en-ZA",
                    TimeZone = "Africa/Johannesburg",
                    DefaultCurrency = "ZAR",
                    Currencies = ["ZAR"],
                    Documents = [],
                    Regions = [],
                    RegionLabel = "Province",
                    PostalLabel = "Postal Code",
                    PostalRequired = false
                }

                ],
                FinancialRules = []
            };
        }
        else if (service == "configuration"
            && method == HttpMethod.Post
            && path == "/country-operations"
            && body is CountryOperationRequest request)
        {
            if (request.CountryCode != "ZA" || request.Currency != "ZAR")
            {
                throw new PlatformException(409, "country_currency_required", "Only the saved country's currency can be connected.");
            }

            result = new CountryOperation
            {
                RequestId = request.RequestId,
                OperationId = request.OperationId ?? Guid.NewGuid(),
                SourceKind = request.SourceKind,
                CountryCode = "ZA",
                Currency = "ZAR",
                PolicyVersion = 1
            };
        }
        else if (service == "configuration" && method == HttpMethod.Post && path.EndsWith("/settle", StringComparison.Ordinal))
        {
            result = new object();
        }
        else
        {
            throw new InvalidOperationException("The fixture attempted an unexpected remote call.");
        }

        return Task.FromResult((T)result);
    }
}
