package com.endava.juniorprogram.usersprogram.services;

import com.endava.juniorprogram.usersprogram.model.User;
import com.endava.juniorprogram.usersprogram.services.repositories.DatabaseRepository;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.stereotype.Service;

import java.util.List;

@Service
public class UsersServiceImpl implements UsersService{

    @Autowired
    private DatabaseRepository repository;

    @Override
    public List<User> getUsers(String name) {
        return repository.getUsers(name);
    }

    @Override
    public User getUser(long id) {
        return repository.getUser(id);
    }

    @Override
    public User deleteUser(long id) {
        return repository.deleteUser(id);
    }

    @Override
    public User addUser(User user) {
        return repository.addUser(user);
    }

    @Override
    public User updateUser(User user) {
        return repository.updateUser(user);
    }

}
