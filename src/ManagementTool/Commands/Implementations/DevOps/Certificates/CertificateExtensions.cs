using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Meshmakers.Octo.Frontend.ManagementTool.Commands.Implementations.DevOps.Certificates;

internal static class CertificateExtensions
{
    /// <summary>
    ///     Encodes the certificate in PEM format for use in Kubernetes.
    /// </summary>
    /// <param name="certificate">The certificate to encode.</param>
    /// <returns>The byte representation of the PEM-encoded certificate.</returns>
    public static byte[] EncodeToPemBytes(this X509Certificate2 certificate)
    {
        return Encoding.UTF8.GetBytes(certificate.EncodeToPem());
    }

    /// <summary>
    ///     Encodes the certificate in PEM format.
    /// </summary>
    /// <param name="certificate">The certificate to encode.</param>
    /// <returns>The string representation of the PEM-encoded certificate.</returns>
    public static string EncodeToPem(this X509Certificate2 certificate)
    {
        return new string(PemEncoding.Write("CERTIFICATE", certificate.RawData));
    }

    /// <summary>
    ///     Encodes the key in PEM format.
    /// </summary>
    /// <param name="key">The key to encode.</param>
    /// <returns>The string representation of the PEM-encoded key.</returns>
    public static string EncodeToPem(this AsymmetricAlgorithm key)
    {
        return new string(PemEncoding.Write("PRIVATE KEY", key.ExportPkcs8PrivateKey()));
    }
}