using FourChanGrabber.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FourChanGrabber.Data;

public class DatabaseSeeder
{
    private readonly MediaDbContext _context;

    public DatabaseSeeder(MediaDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync()
    {
        await EnsureImageSourcesAsync();
    }

    private async Task EnsureImageSourcesAsync()
    {
        var fourChanExists = await _context.ImageSources.AnyAsync(s => s.Name == "4Chan");
        if (!fourChanExists)
        {
            var fourChan = new ImageSource
            {
                Name = "4Chan"
            };

            _context.ImageSources.Add(fourChan);
        }

        await _context.SaveChangesAsync();
    }
}
