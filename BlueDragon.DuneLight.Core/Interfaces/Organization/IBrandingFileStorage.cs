using System;
using System.IO;
using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Core.Interfaces.Organization;

public interface IBrandingFileStorage
{
    /// <summary>
    /// Sprema datoteku na disk i vraća relativni javni URL. Validira ekstenziju, veličinu, deklarirani MIME
    /// (<paramref name="declaredContentType"/>, opcionalan) i stvarni sadržaj/magic bytes; SVG/HTML se odbija
    /// bez obzira na ekstenziju ili deklarirani tip.
    /// </summary>
    Task<string> SaveAsync(Stream content, string originalFileName, string declaredContentType, string organizationSlug, string filePrefix);

    /// <summary>
    /// Obriše datoteku s diska ako se radi o našoj branding datoteci (provjera patha). Best-effort — nikad ne
    /// baca, neuspjeh se samo logira kao upozorenje.
    /// </summary>
    Task DeleteAsync(string publicUrl);

    /// <summary>Čita stream datoteke za javni GET (login screen).</summary>
    Stream OpenRead(string publicUrl, out string contentType);

    /// <summary>Vraća apsolutnu fizičku putanju za provjere postojanja, brisanja itd. Sanitizirana protiv path-traversala.</summary>
    string GetPhysicalPath(string publicUrl);
}
