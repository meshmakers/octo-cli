using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.Certificates;

internal record CertificatePair(X509Certificate2 Certificate, AsymmetricAlgorithm Key);
