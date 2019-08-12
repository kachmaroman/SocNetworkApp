using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SocNetworkApp.API.Helpers;
using SocNetworkApp.API.Models;

namespace SocNetworkApp.API.Data
{
    public class DataRepository : IDataRepository
    {
        private readonly DataContext _context;

        public DataRepository(DataContext context)
        {
            this._context = context;
        }

        public void Add<T>(T entity) where T : class
        {
            _context.Add(entity);
        }

        public void Delete<T>(T entity) where T : class
        {
            _context.Remove(entity);
        }

        public async Task<Like> GetLike(Guid userId, Guid recipientId)
        {
            return await _context.Likes.FirstOrDefaultAsync(u => u.LikerId == userId && u.LikeeId == recipientId);   
        }

        public async Task<Photo> GetMainPhotoForUser(Guid userId)
        {
            return await _context.Photos.FirstOrDefaultAsync(p => p.UserId == userId && p.IsMain);
        }

        public async Task<Photo> GetPhoto(Guid id)
        {
            return await _context.Photos.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<User> GetUser(Guid id, bool isCurrentUser)
        {
            IQueryable<User> query = _context.Users.Include(p => p.Photos).AsQueryable();

            if (isCurrentUser) 
            {
                query = query.IgnoreQueryFilters();
            }

            return await query.FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<PagedList<User>> GetUsers(UserParams userParams)
        {
            IQueryable<User> users = _context.Users.Include(p => p.Photos).OrderByDescending(u => u.LastActive)
                                                                          .AsQueryable();

            if (string.IsNullOrEmpty(userParams.Gender) || userParams.Gender == "all")
            {
               users = users.Where(u => u.Id != userParams.UserId);
            }
            else
            {
                users = users.Where(u => u.Id != userParams.UserId && u.Gender == userParams.Gender);
            }

            if (userParams.Likers)
            {
                IEnumerable<Guid> userLikers = await GetUserLikes(userParams.UserId, userParams.Likers);
                users = users.Where(u => userLikers.Contains(u.Id));
            }

            if (userParams.Likees)
            {
                IEnumerable<Guid> userLikees = await GetUserLikes(userParams.UserId, userParams.Likers);
                users = users.Where(u => userLikees.Contains(u.Id));
            }

            if (userParams.MinAge != 18 || userParams.MaxAge != 99)
            {
                DateTime minDob = DateTime.Today.AddYears(-userParams.MaxAge - 1);
                DateTime maxDob = DateTime.Today.AddYears(-userParams.MinAge);
                users = users.Where(u => u.DateOfBirth >= minDob && u.DateOfBirth <= maxDob);
            }

            if (!string.IsNullOrEmpty(userParams.OrderBy))
            {
                switch (userParams.OrderBy)
                {
                    case "created":
                        users = users.OrderByDescending(u => u.Created);
                        break;
                    default:
                        users = users.OrderByDescending(u => u.LastActive);
                        break;
                }
            }
            
            return await PagedList<User>.CreateAsync(users, userParams.PageNumber, userParams.PageSize);
        }

        private async Task<IEnumerable<Guid>> GetUserLikes(Guid id, bool likers)
        {
            User user = await _context.Users.Include(x => x.Likers)
                                            .Include(x => x.Likees)
                                            .FirstOrDefaultAsync(u => u.Id == id);

            if (likers)
            {
                return user.Likers.Where(u => u.LikeeId == id).Select(u => u.LikerId);
            }
            else
            {
                return user.Likees.Where(u => u.LikerId == id).Select(u => u.LikeeId);
            }
        }

        public async Task<bool> SaveAll()
        {
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<Message> GetMessage(Guid id)
        {
            return await _context.Messages.FirstOrDefaultAsync(m => m.Id == id);
        }


        public async Task<IEnumerable<Message>> GetMessageThread(Guid userId, Guid recipientId)
        {
            List<Message> messages = await _context.Messages
                                            .Include(u => u.Sender).ThenInclude(p => p.Photos)
                                            .Include(u => u.Recipient).ThenInclude(p => p.Photos)
                                            .Where(m => m.RecipientId == userId && !m.RecipientDeleted && m.SenderId == recipientId 
                                                     || m.RecipientId == recipientId  && !m.SenderDeleted && m.SenderId == userId)
                                            .OrderByDescending(m => m.MessageSent)
                                            .ToListAsync();

            return messages;
        }

        public async Task<PagedList<Message>> GetMessagesForUser(MessageParams messageParams)
        {
            IQueryable<Message> messages = _context.Messages
                                            .Include(u => u.Sender).ThenInclude(p => p.Photos)
                                            .Include(u => u.Recipient).ThenInclude(p => p.Photos)
                                            .AsQueryable();

            switch (messageParams.MessageContainer)
            {
                case "Inbox":
                    messages = messages.Where(u => u.RecipientId == messageParams.UserId && !u.RecipientDeleted);
                    break;
                case "Outbox":
                    messages = messages.Where(u => u.SenderId == messageParams.UserId && !u.SenderDeleted);
                    break;
                default:
                    messages = messages.Where(u => u.RecipientId == messageParams.UserId && !u.IsRead && !u.RecipientDeleted);
                    break;
            }

            messages = messages.OrderByDescending(d => d.MessageSent);

            return await PagedList<Message>.CreateAsync(messages, messageParams.PageNumber, messageParams.PageSize);
        }
    }
}